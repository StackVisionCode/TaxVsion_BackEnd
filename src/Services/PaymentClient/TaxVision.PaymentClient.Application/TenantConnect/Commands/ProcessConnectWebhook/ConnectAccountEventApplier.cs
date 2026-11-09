using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;

namespace TaxVision.PaymentClient.Application.TenantConnect.Commands.ProcessConnectWebhook;

/// <summary>Eventos <c>account.updated</c> y <c>capability.updated</c>. Devuelve lo que hay que publicar después de
/// guardar.</summary>
internal static class ConnectAccountEventApplier
{
    public static async Task<Result<IReadOnlyList<object>>> ApplyAsync(
        ConnectWebhookEvent evt,
        TenantConnectAccount account,
        ITenantPaymentConfigRepository configs,
        IPaymentAuditLogWriter audit,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        // Fuera de orden: el estado vigente ya viene de un evento más nuevo.
        if (account.IsStaleProviderEvent(evt.OccurredAtUtc))
            return Result.Success<IReadOnlyList<object>>([]);

        var statusBeforeWebhook = account.Status;
        var updateResult = account.UpdateFromWebhook(
            evt.ChargesEnabled ?? false,
            evt.PayoutsEnabled ?? false,
            evt.RequirementsCurrentlyDue ?? [],
            nowUtc,
            evt.OccurredAtUtc
        );
        if (updateResult.IsFailure)
            return Result.Failure<IReadOnlyList<object>>(updateResult.Error);

        await AuditEntryFactory.AppendAsync(
            audit,
            account.TenantId,
            nameof(TenantConnectAccount),
            account.Id,
            PaymentAuditAction.TenantConnectAccountUpdated,
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                account.Status,
                account.CanCharge,
                account.CanReceivePayouts,
            },
            reason: null,
            nowUtc,
            ct
        );

        if (account.Status == ConnectAccountStatus.Enabled)
        {
            if (statusBeforeWebhook != ConnectAccountStatus.Enabled)
                metrics.RecordConnectOnboardingCompleted();

            var config = await configs.GetByTenantAndProviderAsync(account.TenantId, account.ProviderCode, ct);
            if (config is { Mode: TenantPaymentMode.Connect, IsActive: false })
                config.MarkActiveViaConnect(Guid.Empty, nowUtc);

            return Publish(
                new TenantConnectAccountEnabledIntegrationEvent
                {
                    TenantId = account.TenantId,
                    TenantConnectAccountId = account.Id,
                    CorrelationId = correlation.CorrelationId,
                }
            );
        }

        if (account.Status == ConnectAccountStatus.Restricted)
            return Publish(
                new TenantConnectAccountRestrictedIntegrationEvent
                {
                    TenantId = account.TenantId,
                    TenantConnectAccountId = account.Id,
                    RequirementsCurrentlyDue = account.RequirementsCurrentlyDue,
                    CorrelationId = correlation.CorrelationId,
                }
            );

        if (account.RequirementsCurrentlyDue.Count > 0)
            return Publish(
                new TenantConnectAccountOnboardingRequiredIntegrationEvent
                {
                    TenantId = account.TenantId,
                    TenantConnectAccountId = account.Id,
                    RequirementsCurrentlyDue = account.RequirementsCurrentlyDue,
                    CorrelationId = correlation.CorrelationId,
                }
            );

        return Result.Success<IReadOnlyList<object>>([]);
    }

    private static Result<IReadOnlyList<object>> Publish(object message) =>
        Result.Success<IReadOnlyList<object>>([message]);
}
