using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.Customers.ValueObjects;

/// <summary>
/// De qué operación de otro servicio nació el cliente. Es una marca técnica para no repetir el alta;
/// no compara datos del cliente, así que no es una regla de duplicados.
/// </summary>
public sealed record CustomerOrigin
{
    public const int SourceServiceMaxLength = 64;

    public string SourceService { get; }
    public Guid SourceReferenceId { get; }

    private CustomerOrigin(string sourceService, Guid sourceReferenceId)
    {
        SourceService = sourceService;
        SourceReferenceId = sourceReferenceId;
    }

    public static Result<CustomerOrigin> Create(string sourceService, Guid sourceReferenceId)
    {
        if (string.IsNullOrWhiteSpace(sourceService) || sourceService.Trim().Length > SourceServiceMaxLength)
            return Result.Failure<CustomerOrigin>(
                new Error("CustomerOrigin.SourceService", "Origin source service is invalid.")
            );
        if (sourceReferenceId == Guid.Empty)
            return Result.Failure<CustomerOrigin>(
                new Error("CustomerOrigin.SourceReferenceId", "Origin source reference is required.")
            );

        return Result.Success(new CustomerOrigin(sourceService.Trim(), sourceReferenceId));
    }
}
