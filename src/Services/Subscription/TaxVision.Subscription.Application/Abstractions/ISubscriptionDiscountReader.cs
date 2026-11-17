using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Abstractions;

/// <summary>
/// Lee el descuento vigente de la suscripción para un cobro sin cargar el agregado: lo usan los cobros de asientos y
/// add-ons, que trabajan sobre sus propios agregados. Solo lectura.
/// </summary>
public interface ISubscriptionDiscountReader
{
    Task<(long Cents, DiscountSource? Source, string? Label)> ForChargeAsync(
        Guid tenantId,
        DiscountChargeKind kind,
        long grossCents,
        string currency,
        DateTime chargeDateUtc,
        CancellationToken ct = default
    );
}
