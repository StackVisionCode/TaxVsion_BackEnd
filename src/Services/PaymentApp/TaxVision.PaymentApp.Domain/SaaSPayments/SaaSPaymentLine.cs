using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Domain.SaaSPayments;

/// <summary>Una cosa cobrada dentro de un pago con varias (p. ej. el plan, un add-on o los asientos de un cambio a anual).</summary>
public sealed class SaaSPaymentLine
{
    public const int DescriptionMaxLength = 200;

    private SaaSPaymentLine() { }

    public Guid Id { get; private set; }
    public string Description { get; private set; } = default!;
    public int Quantity { get; private set; }
    public long UnitAmountCents { get; private set; }

    public long AmountCents => Quantity * UnitAmountCents;

    public static Result<SaaSPaymentLine> Create(string description, int quantity, long unitAmountCents)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > DescriptionMaxLength)
            return Result.Failure<SaaSPaymentLine>(
                new Error("SaaSPaymentLine.InvalidDescription", "A line needs a description of up to 200 characters.")
            );

        if (quantity < 1 || unitAmountCents < 0)
            return Result.Failure<SaaSPaymentLine>(
                new Error("SaaSPaymentLine.InvalidAmount", "A line needs at least one unit and a non-negative price.")
            );

        return Result.Success(
            new SaaSPaymentLine
            {
                Id = Guid.NewGuid(),
                Description = trimmed,
                Quantity = quantity,
                UnitAmountCents = unitAmountCents,
            }
        );
    }
}

/// <summary>Una línea del pago, tal como viaja en el snapshot y en los eventos.</summary>
public sealed record SaaSPaymentLineSnapshot(string Description, int Quantity, long UnitAmountCents);
