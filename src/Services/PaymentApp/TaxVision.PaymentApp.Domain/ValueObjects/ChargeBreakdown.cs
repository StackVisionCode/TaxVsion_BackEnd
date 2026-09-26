using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Domain.ValueObjects;

/// <summary>
/// Cómo se compone el importe de un cobro: tantas unidades a tal precio. Existe para que el recibo pueda
/// decir "3 × $10.53" en vez de solo el total.
///
/// Es opcional a propósito: un cobro sin unidades que contar —una prorrata de cambio de plan— no lo lleva y
/// el recibo muestra únicamente el total. Y cuando lo lleva, <see cref="SaaSPayments.SaaSPayment"/> exige que
/// cuadre con el importe cobrado: un recibo cuyas cuentas no salen es peor que uno escueto.
/// </summary>
public sealed record ChargeBreakdown
{
    public int Quantity { get; }
    public long UnitAmountCents { get; }

    private ChargeBreakdown(int quantity, long unitAmountCents)
    {
        Quantity = quantity;
        UnitAmountCents = unitAmountCents;
    }

    public long TotalCents => Quantity * UnitAmountCents;

    public static Result<ChargeBreakdown> Create(int quantity, long unitAmountCents)
    {
        if (quantity <= 0)
            return Result.Failure<ChargeBreakdown>(
                new Error("ChargeBreakdown.InvalidQuantity", "Quantity must be greater than zero.")
            );

        if (unitAmountCents <= 0)
            return Result.Failure<ChargeBreakdown>(
                new Error("ChargeBreakdown.InvalidUnitAmount", "Unit amount must be greater than zero.")
            );

        return Result.Success(new ChargeBreakdown(quantity, unitAmountCents));
    }
}
