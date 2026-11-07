using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.Messages;
using Wolverine;
using Wolverine.Attributes;

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

/// <summary>Una unidad de la conciliación: aplica el reporte del proveedor o, sin <paramref name="Report"/>, cierra un
/// envío que nunca tuvo confirmación (D-SMS-3).</summary>
public sealed record ApplySmsReconciliationCommand(Guid TenantId, Guid MessageId, SmsDeliveryUpdate? Report);

public sealed record ApplySmsReconciliationResponse(bool Updated, SmsBatchToSettle? BatchToSettle);

/// <summary>
/// Orquestador sin transacción (BB-8): lee los mensajes y le pregunta a cada proveedor fuera de cualquier transacción.
/// Cada cambio se aplica en su propia unidad, que relee la fila: la consulta puede haber quedado vieja.
/// </summary>
public static class ReconcileSmsStatusesHandler
{
    [NonTransactional]
    public static async Task<Result<ReconcileSmsStatusesResponse>> Handle(
        ReconcileSmsStatusesCommand command,
        ISmsAdapterFactory adapters,
        ISmsMessageRepository messages,
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

        var units = new List<ApplySmsReconciliationCommand>();
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
                if (byProviderId.TryGetValue(update.ProviderMessageId, out var message) && CanChange(message, update))
                    units.Add(new ApplySmsReconciliationCommand(message.TenantId, message.Id, update));
        }

        // Sin confirmación a tiempo: se cierra sin reenviar y su unidad no se cobra (D-SMS-3). Va después de los
        // reportes: lo que el pull resolvió ya no está sin resolver.
        units.AddRange(unconfirmed.Select(m => new ApplySmsReconciliationCommand(m.TenantId, m.Id, null)));

        var updated = 0;
        var batches = new HashSet<SmsBatchToSettle>();
        foreach (var unit in units)
        {
            ApplySmsReconciliationResponse applied;
            try
            {
                applied = await bus.InvokeAsync<ApplySmsReconciliationResponse>(unit, ct);
            }
            catch (ConflictException ex) when (ex.Code == ConcurrencyConflictCode)
            {
                // El webhook escribió el mensaje en este momento: un mensaje no corta la vuelta.
                logger.LogInformation(
                    "SMS {MessageId} changed during reconciliation; next run reads it.",
                    unit.MessageId
                );
                continue;
            }

            if (applied.Updated)
                updated++;
            if (applied.BatchToSettle is { } batch)
                batches.Add(batch);
        }

        var examined = stuck.Concat(unconfirmed).Select(m => m.Id).Distinct().Count();
        logger.LogInformation(
            "SMS reconciliation examined {Examined} message(s); updated {Updated}.",
            examined,
            updated
        );
        return Result.Success(new ReconcileSmsStatusesResponse(examined, updated, [.. batches]));
    }

    private const string ConcurrencyConflictCode = "Persistence.ConcurrencyConflict";

    // Accepted solo resuelve un resultado desconocido; en los demás, el proveedor todavía no tiene nada nuevo.
    private static bool CanChange(SmsMessage message, SmsDeliveryUpdate update) =>
        update.Status != SmsCanonicalStatus.Accepted || message.Status == SmsMessageStatus.OutcomeUnknown;
}

public static class ApplySmsReconciliationHandler
{
    public static async Task<ApplySmsReconciliationResponse> Handle(
        ApplySmsReconciliationCommand command,
        ISmsMessageRepository messages,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var message = await messages.GetByIdAsync(command.TenantId, command.MessageId, ct);
        if (message is null)
            return new ApplySmsReconciliationResponse(false, null);

        var nowUtc = DateTime.UtcNow;
        var before = message.Status;
        var wasUnresolved = message.IsUnresolved;
        if (command.Report is { } report)
            SmsDeliveryOutcome.Apply(message, report, nowUtc);
        else if (wasUnresolved)
            message.MarkFailed(nowUtc, SmsErrors.ProviderOutcomeUnknown.Code, "No provider confirmation.");

        if (message.Status == before)
            return new ApplySmsReconciliationResponse(false, null);

        // Guardar antes de publicar. La liquidación sale con el estado: si no, un Wallet caído la pierde.
        await unitOfWork.SaveChangesAsync(ct);
        await SmsDeliveryOutcome.PublishAsync(bus, message);

        if (!wasUnresolved || !message.WalletCharged)
            return new ApplySmsReconciliationResponse(true, null);

        var batch = new SmsBatchToSettle(message.TenantId, message.BatchId);
        await bus.PublishAsync(new SettleSmsBatchCommand(batch.TenantId, batch.BatchId));
        return new ApplySmsReconciliationResponse(true, batch);
    }
}

/// <summary>Conciliación completa: cada estado y el pedido de liquidación de su lote, en una transacción por mensaje.</summary>
public static class SmsReconciliation
{
    public static Task<Result<ReconcileSmsStatusesResponse>> RunAsync(
        IMessageBus bus,
        ReconcileSmsStatusesCommand command,
        CancellationToken ct
    ) => bus.InvokeAsync<Result<ReconcileSmsStatusesResponse>>(command, ct);
}
