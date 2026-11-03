using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>
/// Años que la oficina conserva los registros de clientes; es la retención que usan los módulos que borran
/// por antigüedad (borrar sigue siendo opt-in en cada uno). Mínimo 3 (Form 8879 y §6107), máximo 20 (el
/// techo de Signature). null = sin política (cada módulo usa su default).
/// </summary>
public sealed record RecordRetentionYears
{
    public const int Min = 3;
    public const int Max = 20;

    public static readonly Error OutOfRange = new(
        "Tenant.Compliance.RetentionYears",
        $"Retention must be between {Min} and {Max} years."
    );

    public int Value { get; }

    private RecordRetentionYears(int value) => Value = value;

    public static Result<RecordRetentionYears?> CreateOptional(int? years)
    {
        if (years is null)
            return Result.Success<RecordRetentionYears?>(null);

        return years is < Min or > Max
            ? Result.Failure<RecordRetentionYears?>(OutOfRange)
            : Result.Success<RecordRetentionYears?>(new(years.Value));
    }
}
