using BuildingBlocks.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using Wolverine;

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
        var tenants = services.GetRequiredService<ITenantRegistry>();
        var bus = services.GetRequiredService<IMessageBus>();
        var correlation = services.GetRequiredService<ICorrelationContext>();
        var logger = services.GetRequiredService<ILogger<MissingReceiptBackfillJob>>();

        var pending = await payments.GetSucceededWithoutReceiptAsync(DateTime.UtcNow - Grace, BatchSize, ct);
        if (pending.Count == 0)
            return;

        foreach (var payment in pending)
        {
            using (correlation.Push(Guid.NewGuid().ToString("N")))
            {
                await SaaSPaymentResultPublisher.PublishReceiptRequestAsync(
                    payment,
                    bus,
                    correlation.CorrelationId,
                    tenants,
                    ct
                );
            }
        }

        logger.LogInformation("MissingReceiptBackfillJob re-requested {Count} receipt(s).", pending.Count);
    }
}
