using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Providers;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Baja el correo completo, lee el informe de entrega y avisa a Postmaster un evento por destinatario.
/// Un correo que solo parecía aviso (no trae informe) se descarta sin ruido. Sin transacción: no escribe en la base,
/// y los avisos salen por el outbox durable al terminar.
/// </summary>
public static class InspectDeliveryReportHandler
{
    private const int MaxDiagnosticLength = 500;
    private const int MaxSummaryLength = 500;
    private const int MaxSubjectLength = 500;

    [NonTransactional]
    public static async Task Handle(
        InspectDeliveryReport cmd,
        ITenantEmailAccountRepository accountRepository,
        IEmailProviderClientFactory emailClientFactory,
        IDeliveryReportParser parser,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(cmd.CorrelationId))
        {
            var account = await accountRepository.GetByIdAsync(cmd.AccountId, ct);
            if (account.IsFailure)
                return;

            var client = emailClientFactory.Resolve(account.Value.ProviderCode);
            if (client.IsFailure)
                return;

            byte[] raw;
            try
            {
                raw = await client.Value.GetRawMessageAsync(cmd.AccountId, cmd.TenantId, cmd.ProviderMessageId, ct);
            }
            catch (EmailProviderException ex) when (ex.RequiresReconnect)
            {
                // Sin token no hay forma de leerlo; la cuenta ya avisa que hay que reconectar.
                logger.LogInformation(
                    "Delivery report on account {AccountId} skipped: reconnect required.",
                    cmd.AccountId
                );
                return;
            }

            var report = parser.Parse(raw);
            foreach (var entry in report.Entries)
                await PublishAsync(cmd, report, entry, correlation.CorrelationId, bus);
        }
    }

    private static async Task PublishAsync(
        InspectDeliveryReport cmd,
        DeliveryReport report,
        DeliveryReportEntry entry,
        string correlationId,
        IMessageBus bus
    )
    {
        // Sin el correo original o sin destinatario no hay nada que atar del otro lado.
        if (string.IsNullOrWhiteSpace(entry.OriginalMessageId) || string.IsNullOrWhiteSpace(entry.Recipient))
            return;

        await bus.PublishAsync(
            new ConnectorsDeliveryReportDetectedIntegrationEvent
            {
                TenantId = cmd.TenantId,
                CorrelationId = correlationId,
                AccountId = cmd.AccountId,
                ReportProviderMessageId = cmd.ProviderMessageId,
                OriginalInternetMessageId = entry.OriginalMessageId,
                Recipient = entry.Recipient,
                Action = entry.Action,
                StatusCode = entry.StatusCode,
                DiagnosticSnippet = Truncate(entry.Diagnostic, MaxDiagnosticLength),
                ReportedAtUtc = cmd.ReceivedAtUtc,
                ReportSubject = Truncate(report.Subject, MaxSubjectLength),
                ReportFrom = report.From,
                ReportSummary = Truncate(report.Summary, MaxSummaryLength),
            }
        );
    }

    private static string? Truncate(string? value, int max) =>
        value is not null && value.Length > max ? value[..max] : value;
}
