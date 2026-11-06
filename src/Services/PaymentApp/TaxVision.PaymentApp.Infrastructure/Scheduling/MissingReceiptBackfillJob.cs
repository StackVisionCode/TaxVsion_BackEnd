using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments.Events;
using TaxVision.PaymentApp.Infrastructure.Persistence;

namespace TaxVision.PaymentApp.Infrastructure.Scheduling;

/// <summary>
/// Vuelve a pedir el recibo de un cobro confirmado que se quedó sin él. El pedido solo se emite en el
/// instante en que el pago se confirma, así que cualquier tropiezo en la cadena —Documents caído, el archivo
/// rechazado por almacenamiento, el evento de vuelta perdido— dejaba al tenant sin su recibo **para
/// siempre**, y nadie se enteraba. Encontrado así: una compra real acabó pagada y sin recibo.
///
/// Solo se reenvía el pedido de recibo, nunca el evento por tipo de cobro: reaprovisionar asientos o add-ons
/// ya entregados sería mucho peor que no tener el PDF. Del lado de Documents el pedido es idempotente por
/// <c>saas-receipt:{paymentId}</c>, y una generación que se quedó a medias hace rato se reintenta.
/// Cada pedido sale por el outbox de su propia unidad (BB-3); no guarda nada en el pago.
/// </summary>
public sealed class MissingReceiptBackfillJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<MissingReceiptBackfillJob> logger
) : PeriodicPaymentAppJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(14))
{
    private const int BatchSize = 50;

    /// <summary>Margen para que el camino normal termine antes de que esto se meta.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

    protected override string JobName => "missing-receipt-backfill";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var payments = services.GetRequiredService<ISaaSPaymentRepository>();
        var logger = services.GetRequiredService<ILogger<MissingReceiptBackfillJob>>();

        var pending = await payments.GetSucceededWithoutReceiptAsync(DateTime.UtcNow - Grace, BatchSize, ct);
        if (pending.Count == 0)
            return;

        var summary = await RequestAsync([.. pending.Select(payment => payment.Snapshot())], ct);
        logger.LogInformation("MissingReceiptBackfillJob re-requested {Count} receipt(s).", summary.Committed);
    }

    internal Task<JobBatchSummary> RequestAsync(IReadOnlyList<SaaSPaymentSnapshot> payments, CancellationToken ct) =>
        RunUnitsAsync(payments, RequestOneAsync, ct);

    private static async Task<bool> RequestOneAsync(
        SaaSPaymentSnapshot payment,
        JobUnitContext<PaymentAppDbContext> unit,
        CancellationToken ct
    )
    {
        // Un id de traza por pedido: cada recibo se sigue por separado.
        var receipt = await SaaSPaymentResultMessages.ReceiptRequestAsync(
            payment,
            Guid.NewGuid().ToString("N"),
            unit.Services.GetRequiredService<ITenantRegistry>(),
            ct
        );
        if (receipt is null)
            return false;

        await unit.PublishAsync(receipt);
        return true;
    }
}
