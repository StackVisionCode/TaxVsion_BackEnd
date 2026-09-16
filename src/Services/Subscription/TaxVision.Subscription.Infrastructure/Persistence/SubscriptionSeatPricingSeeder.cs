using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence;

/// <summary>
/// Siembra el catálogo GLOBAL singleton de precios de asiento (una sola fila con Id fijo). Idempotente:
/// no hace nada si ya existe. Precio armonizado a los planes (anual = mensual × 10, "2 meses gratis").
/// Precios de arranque — PlatformAdmin los edita por endpoint (<c>SetSeatPrices</c>). Se siembran los 5
/// <see cref="SeatType"/> para que <c>PurchaseSeatsHandler</c> siempre resuelva un precio: el asiento de
/// staff extra (<see cref="SeatType.Standard"/>) con precio real; el resto en 0 (placeholder editable),
/// conservando el comportamiento gratuito actual hasta que el admin les fije precio.
/// </summary>
public static class SubscriptionSeatPricingSeeder
{
    // Singleton: un único catálogo de precios de asiento para toda la plataforma.
    public static readonly Guid SeatPricingId = new("e1000000-0000-0000-0000-000000000001");

    // Precio mensual USD por tipo de asiento. Standard = staff extra (precio real); resto = 0 placeholder.
    // Standard $15/mo ($150/yr con ×10): por encima del per-seat implícito del plan más alto (Enterprise
    // $299/25 = ~$12) para que comprar asientos sueltos nunca canibalice el upgrade de plan; ancla estándar
    // de la industria (~$15/usuario/mes). Editable en vivo por PlatformAdmin (SetSeatPrices).
    private static readonly (SeatType Type, decimal MonthlyUsd)[] SeatPrices =
    [
        (SeatType.Standard, 15m),
        (SeatType.Portal, 0m),
        (SeatType.Signature, 0m),
        (SeatType.ReadOnly, 0m),
        (SeatType.ServiceAccount, 0m),
    ];

    public static async Task SeedAsync(SubscriptionDbContext db, CancellationToken ct)
    {
        if (await db.SeatPricings.AnyAsync(ct))
            return;

        var nowUtc = DateTime.UtcNow;
        var pricing = SeatPricing.Seed(SeatPricingId, nowUtc);

        foreach (var (type, monthlyUsd) in SeatPrices)
        {
            pricing.AddPriceTier(
                SeatPriceTier
                    .Create(pricing.Id, type, BillingCycle.Monthly, Money.Create(monthlyUsd, "USD").Value)
                    .Value
            );
            pricing.AddPriceTier(
                SeatPriceTier
                    .Create(pricing.Id, type, BillingCycle.Yearly, Money.Create(monthlyUsd * 10m, "USD").Value)
                    .Value
            );
        }

        db.SeatPricings.Add(pricing);
        await db.SaveChangesAsync(ct);
    }
}
