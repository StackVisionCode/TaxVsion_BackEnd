using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.ValueObjects;

/// <summary>
/// Contexto fiscal: de qué cliente y de qué año. Los dos IDs son opacos — Task no llama a Customer
/// para validarlos, el nombre se resuelve por proyección al leer. Ambos son opcionales por separado.
/// </summary>
public sealed record TaskReference
{
    /// <summary>Un año fuera de este rango es un error de tipeo, no un dato.</summary>
    public const int MinTaxYear = 1990;

    public const int MaxTaxYear = 2100;

    public static readonly TaskReference None = new(null, null);

    public Guid? CustomerId { get; }
    public int? TaxYear { get; }

    private TaskReference(Guid? customerId, int? taxYear)
    {
        CustomerId = customerId;
        TaxYear = taxYear;
    }

    public static Result<TaskReference> Create(Guid? customerId, int? taxYear)
    {
        if (customerId == Guid.Empty)
            return Result.Failure<TaskReference>(TaskErrors.Reference.CustomerInvalid);

        if (taxYear is { } year && (year < MinTaxYear || year > MaxTaxYear))
            return Result.Failure<TaskReference>(TaskErrors.Reference.TaxYearOutOfRange);

        return Result.Success(new TaskReference(customerId, taxYear));
    }
}
