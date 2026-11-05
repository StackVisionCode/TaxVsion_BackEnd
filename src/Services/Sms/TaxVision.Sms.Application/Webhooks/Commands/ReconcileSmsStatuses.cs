using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.Messages;
using Wolverine;

namespace TaxVision.Sms.Application.Webhooks.Commands;

/// <summary>
/// Reconciliación de estado por PULL (agnóstica de proveedor). Toma los mensajes atascados en
/// <see cref="SmsMessageStatus.Accepted"/> (el proveedor los aceptó pero no llegó un DLR final por webhook —
/// sea porque el proveedor no alcanza nuestra URL pública, típico en dev local, o porque el DLR se perdió),
/// consulta a cada proveedor por su estado real (<see cref="ISmsProvider.FetchDeliveryReportsAsync"/>) y aplica
/// la transición canónica (Delivered/Failed/Undeliverable), publicando el evento de resultado igual que la ruta
/// del webhook. El proveedor de cada mensaje sale del propio mensaje (<c>ProviderCode</c>) — el orquestador no
/// hace <c>switch(provider)</c>. Idempotente: releer un estado ya aplicado es un no-op (transiciones del dominio).
/// También cierra sin reenviar los envíos que nunca tuvieron confirmación (D-SMS-3).
/// </summary>
/// <param name="TenantId">Si se indica, solo ese tenant (endpoint manual); null = todos (job de fondo).</param>
/// <param name="MaxMessages">Tope de mensajes a reconciliar por corrida.</param>
/// <param name="MinAgeSeconds">Antigüedad mínima desde el último cambio para considerar un mensaje atascado
/// (evita competir con un DLR que quizá está por llegar).</param>
/// <param name="PendingTimeoutMinutes">Una fila Pending sin id del proveedor más vieja que esto quedó a medias.</param>
/// <param name="OutcomeUnknownTimeoutMinutes">Un resultado desconocido sin confirmación más viejo que esto se
/// cierra sin cobro (vencimiento de la reserva <c>individual-sms</c>, D-WAL-TTL).</param>
public sealed record ReconcileSmsStatusesCommand(
    Guid? TenantId = null,
    int MaxMessages = 200,
    int MinAgeSeconds = 60,
    int PendingTimeoutMinutes = 15,
    int OutcomeUnknownTimeoutMinutes = 60
);

/// <summary>Lote cobrado al Wallet que quedó resuelto y hay que liquidar.</summary>
public sealed record SmsBatchToSettle(Guid TenantId, Guid BatchId);

public sealed record ReconcileSmsStatusesResponse(
    int Examined,
    int Updated,
    IReadOnlyList<SmsBatchToSettle>? BatchesToSettle = null
);

public static class ReconcileSmsStatusesHandler
{
    public static async Task<Result<ReconcileSmsStatusesResponse>> Handle(
        ReconcileSmsStatusesCommand command,
        ISmsAdapterFactory adapters,
        ISmsMessageRepository messages,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<ReconcileSmsStatusesCommand> logger,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var olderThanUtc = nowUtc.AddSeconds(-Math.Max(0, command.MinAgeSeconds));
        var limit = Math.Clamp(command.MaxMessages, 1, 1000);

        var stuck = await messages.GetStuckForReconciliationAsync(command.TenantId, olderThanUtc, limit, ct);
        var unconfirmed = await messages.GetUnconfirmedSendsAsync(
            command.TenantId,
            nowUtc.AddMinutes(-Math.Max(1, command.PendingTimeoutMinutes)),
            nowUtc.AddMinutes(-Math.Max(1, command.OutcomeUnknownTimeoutMinutes)),
            limit,
            ct
        );
        if (stuck.Count == 0 && unconfirmed.Count == 0)
            return Result.Success(new ReconcileSmsStatusesResponse(0, 0));

        var updated = 0;
        var batches = new HashSet<SmsBatchToSettle>();
        foreach (var group in stuck.GroupBy(m => m.ProviderCode, StringComparer.OrdinalIgnoreCase))
        {
            ISmsProvider provider;
            try
            {
                provider = adapters.Resolve(group.Key);
            }
            catch (InvalidOperationException)
            {
                // Proveedor de un mensaje viejo ya no registrado: se deja como está (no se inventa estado).
                continue;
            }

            if (!provider.Capabilities.SupportsStatusPull)
                continue;

            var byProviderId = group
                .Where(m => m.ProviderMessageId is not null)
                .GroupBy(m => m.ProviderMessageId!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var fetched = await provider.FetchDeliveryReportsAsync([.. byProviderId.Keys], ct);
            if (fetched.IsFailure)
                continue;

            foreach (var update in fetched.Value)
            {
                if (!byProviderId.TryGetValue(update.ProviderMessageId, out var message))
                    continue;

                var before = message.Status;
                var wasUnresolved = message.IsUnresolved;
                ApplyTransition(message, update, nowUtc);
                if (message.Status == before)
                    continue; // sin cambio (aún PENDING, o ya en ese estado) → no-op

                await PublishOutcomeAsync(bus, message, ct);
                updated++;
                if (wasUnresolved && message.WalletCharged)
                    batches.Add(new SmsBatchToSettle(message.TenantId, message.BatchId));
            }
        }

        // Sin confirmación a tiempo: se cierra sin reenviar y su unidad no se cobra (D-SMS-3).
        foreach (var message in unconfirmed)
        {
            if (!message.IsUnresolved)
                continue; // lo resolvió el pull de arriba
            message.MarkFailed(nowUtc, SmsErrors.ProviderOutcomeUnknown.Code, "No provider confirmation.");
            await PublishOutcomeAsync(bus, message, ct);
            updated++;
            if (message.WalletCharged)
                batches.Add(new SmsBatchToSettle(message.TenantId, message.BatchId));
        }

        if (updated > 0)
            await unitOfWork.SaveChangesAsync(ct);

        var examined = stuck.Concat(unconfirmed).Select(m => m.Id).Distinct().Count();
        logger.LogInformation(
            "SMS reconciliation examined {Examined} message(s); updated {Updated}.",
            examined,
            updated
        );
        return Result.Success(new ReconcileSmsStatusesResponse(examined, updated, [.. batches]));
    }

    private static void ApplyTransition(SmsMessage message, SmsDeliveryUpdate update, DateTime nowUtc)
    {
        _ = update.Status switch
        {
            SmsCanonicalStatus.Delivered => message.MarkDelivered(nowUtc),
            SmsCanonicalStatus.Failed => message.MarkFailed(nowUtc, update.FailureCode, update.FailureReason),
            SmsCanonicalStatus.Undeliverable => message.MarkUndeliverable(
                nowUtc,
                update.FailureCode,
                update.FailureReason
            ),
            // El proveedor lo tiene: un resultado desconocido pasa a aceptado.
            SmsCanonicalStatus.Accepted when message.Status == SmsMessageStatus.OutcomeUnknown =>
                message.MarkAccepted(message.ProviderMessageId ?? update.ProviderMessageId, nowUtc),
            _ => Result.Success(), // Accepted/PENDING: nada que reconciliar todavía
        };
    }

    private static async Task PublishOutcomeAsync(IMessageBus bus, SmsMessage message, CancellationToken ct)
    {
        bus.TenantId = message.TenantId.ToString();
        if (message.Status == SmsMessageStatus.Accepted)
            await bus.PublishAsync(
                new SmsMessageAcceptedIntegrationEvent
                {
                    TenantId = message.TenantId,
                    CorrelationId = message.CorrelationId,
                    MessageId = message.Id,
                    CustomerId = message.CustomerId,
                    SourceContext = message.SourceContext,
                    ProviderMessageId = message.ProviderMessageId,
                }
            );
        else if (message.Status == SmsMessageStatus.Delivered)
            await bus.PublishAsync(
                new SmsMessageDeliveredIntegrationEvent
                {
                    TenantId = message.TenantId,
                    CorrelationId = message.CorrelationId,
                    MessageId = message.Id,
                    CustomerId = message.CustomerId,
                    SourceContext = message.SourceContext,
                    ProviderMessageId = message.ProviderMessageId,
                }
            );
        else if (message.Status is SmsMessageStatus.Failed or SmsMessageStatus.Undeliverable)
            await bus.PublishAsync(
                new SmsMessageFailedIntegrationEvent
                {
                    TenantId = message.TenantId,
                    CorrelationId = message.CorrelationId,
                    MessageId = message.Id,
                    CustomerId = message.CustomerId,
                    SourceContext = message.SourceContext,
                    ProviderMessageId = message.ProviderMessageId,
                    FailureCode = message.FailureCode,
                }
            );
    }
}

/// <summary>Conciliación completa: estados en su transacción y, después, la liquidación de los lotes resueltos.</summary>
public static class SmsReconciliation
{
    public static async Task<Result<ReconcileSmsStatusesResponse>> RunAsync(
        IMessageBus bus,
        ReconcileSmsStatusesCommand command,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<ReconcileSmsStatusesResponse>>(command, ct);
        if (result.IsFailure)
            return result;

        foreach (var batch in result.Value.BatchesToSettle ?? [])
            await bus.InvokeAsync<SettleSmsBatchResponse>(new SettleSmsBatchCommand(batch.TenantId, batch.BatchId), ct);
        return result;
    }
}
