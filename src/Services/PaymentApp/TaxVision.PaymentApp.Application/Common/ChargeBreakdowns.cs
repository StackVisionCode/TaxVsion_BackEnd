using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Common;

/// <summary>
/// Traduce el desglose opcional que llega por el contrato interno. Solo se acepta si cuadra con el importe
/// que se va a cobrar; si no, el cobro sigue adelante sin desglose y el recibo muestra solo el total. Un
/// número que no cuadra en un recibo es peor que un dato de menos, y perder la compra por eso sería peor aún.
/// </summary>
public static class ChargeBreakdowns
{
    public static ChargeBreakdown? FromRequest(int? quantity, long? unitAmountCents, long amountCents)
    {
        if (quantity is not { } units || unitAmountCents is not { } unitPrice)
            return null;

        var breakdown = ChargeBreakdown.Create(units, unitPrice);
        return breakdown.IsSuccess && breakdown.Value.TotalCents == amountCents ? breakdown.Value : null;
    }
}
