namespace TaxVision.Subscription.Application.Common;

/// <summary>
/// Precio unitario que el recibo puede mostrar sin mentir: el total prorrateado repartido entre las unidades.
/// Solo cuando reparte exacto — si el prorrateo dejó céntimos sueltos, el recibo muestra únicamente el total.
/// </summary>
internal static class ProratedUnit
{
    public static long? Of(long proratedTotalCents, int quantity) =>
        quantity > 0 && proratedTotalCents % quantity == 0 ? proratedTotalCents / quantity : null;
}
