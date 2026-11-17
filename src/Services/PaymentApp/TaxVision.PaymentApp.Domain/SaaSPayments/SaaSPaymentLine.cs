using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Domain.SaaSPayments;

/// <summary>Una cosa cobrada dentro de un pago con varias (p. ej. el plan, un add-on o los asientos de un cambio a anual).</summary>
public sealed class SaaSPaymentLine
{
    public const int DescriptionMaxLength = 200;
    public const int TargetKindMaxLength = 40;

    private SaaSPaymentLine() { }

    public Guid Id { get; private set; }
    public string Description { get; private set; } = default!;
    public int Quantity { get; private set; }
    public long UnitAmountCents { get; private set; }

    /// <summary>A qué pertenece la línea (Plan | Seat | AddOn) y su id. Null en los cobros de antes.</summary>
    public string? TargetKind { get; private set; }
    public Guid? TargetId { get; private set; }

    public long AmountCents => Quantity * UnitAmountCents;

    public static Result<SaaSPaymentLine> Create(
        string description,
        int quantity,
        long unitAmountCents,
        string? targetKind = null,
        Guid? targetId = null
    )
    {
        var kind = string.IsNullOrWhiteSpace(targetKind) ? null : targetKind.Trim();
        if (kind?.Length > TargetKindMaxLength)
            return Result.Failure<SaaSPaymentLine>(
                new Error("SaaSPaymentLine.InvalidTarget", "A line target kind can have up to 40 characters.")
            );

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
                TargetKind = kind,
                TargetId = targetId,
            }
        );
    }
}

/// <summary>Una línea del pago, tal como viaja en el snapshot y en los eventos.</summary>
public sealed record SaaSPaymentLineSnapshot(
    string Description,
    int Quantity,
    long UnitAmountCents,
    string? TargetKind = null,
    Guid? TargetId = null
);
