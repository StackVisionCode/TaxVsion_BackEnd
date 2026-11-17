using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.Definitions;

/// <summary>Duración del descuento: parte del beneficio, se versiona con la regla del código.</summary>
public sealed class CodeDuration
{
    public const int MaxMonths = 36;

    private CodeDuration() { }

    private CodeDuration(CodeDurationKind kind, int? months)
    {
        Kind = kind;
        Months = months;
    }

    public CodeDurationKind Kind { get; private set; }
    public int? Months { get; private set; }

    public static CodeDuration Once() => new(CodeDurationKind.Once, null);

    public static CodeDuration Forever() => new(CodeDurationKind.Forever, null);

    public static Result<CodeDuration> ForMonths(int months) =>
        months is < 1 or > MaxMonths
            ? Result.Failure<CodeDuration>(
                new Error("Codes.CodeDuration.InvalidMonths", $"Months must be between 1 and {MaxMonths}.")
            )
            : Result.Success(new CodeDuration(CodeDurationKind.Months, months));

    public static Result<CodeDuration> Create(CodeDurationKind kind, int? months) =>
        kind switch
        {
            CodeDurationKind.Once when months is null => Result.Success(Once()),
            CodeDurationKind.Forever when months is null => Result.Success(Forever()),
            CodeDurationKind.Months when months is { } value => ForMonths(value),
            _ => Result.Failure<CodeDuration>(
                new Error("Codes.CodeDuration.Invalid", "Months is required only for a Months duration.")
            ),
        };
}
