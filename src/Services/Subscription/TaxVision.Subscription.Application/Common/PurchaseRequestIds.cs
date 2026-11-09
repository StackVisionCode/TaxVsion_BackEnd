using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Subscription.Application.Common;

/// <summary>
/// SUB-12: ids derivados de (tenant, Idempotency-Key) en la compra directa. La misma clave cae en las mismas
/// filas y, por eso, en la misma clave de cobro hacia PaymentApp (<c>addon-initial-…</c>, <c>seat-initial-…</c>).
/// </summary>
public static class PurchaseRequestIds
{
    public static Guid AddOn(Guid tenantId, string clientKey) => Derive($"addon|{tenantId:N}|{clientKey.Trim()}");

    public static Guid Seat(Guid tenantId, string clientKey, int index) =>
        Derive($"seat|{tenantId:N}|{clientKey.Trim()}|{index}");

    private static Guid Derive(string seed) => new(SHA256.HashData(Encoding.UTF8.GetBytes(seed)).AsSpan(0, 16));
}
