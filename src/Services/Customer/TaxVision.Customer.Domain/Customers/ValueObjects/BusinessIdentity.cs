using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.Customers.ValueObjects;

public sealed record BusinessIdentity
{
    public string LegalName { get; }
    public string? Dba { get; }
    public BusinessStructure Structure { get; }
    public DateOnly? FormationDate { get; }
    public Guid? PrincipalBusinessActivityId { get; }

    private BusinessIdentity(
        string legalName,
        string? dba,
        BusinessStructure structure,
        DateOnly? formationDate,
        Guid? principalBusinessActivityId
    )
    {
        LegalName = legalName;
        Dba = string.IsNullOrWhiteSpace(dba) ? null : dba.Trim();
        Structure = structure;
        FormationDate = formationDate;
        PrincipalBusinessActivityId = principalBusinessActivityId;
    }

    public static Result<BusinessIdentity> Create(
        string legalName,
        BusinessStructure structure,
        string? dba = null,
        DateOnly? formationDate = null,
        Guid? principalBusinessActivityId = null
    )
    {
        if (string.IsNullOrWhiteSpace(legalName))
            return Result.Failure<BusinessIdentity>(new Error("BusinessIdentity.LegalName", "Legal name is required."));
        if (formationDate.HasValue && formationDate.Value > DateOnly.FromDateTime(DateTime.UtcNow))
            return Result.Failure<BusinessIdentity>(
                new Error("BusinessIdentity.FormationDate", "Formation date cannot be in the future.")
            );

        return Result.Success(
            new BusinessIdentity(legalName.Trim(), dba, structure, formationDate, principalBusinessActivityId)
        );
    }
}
