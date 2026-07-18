using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.Catalogs;

public sealed class PrincipalBusinessActivity : BaseEntity
{
    private PrincipalBusinessActivity() { }

    public string NaicsCode { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string? Sector { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<PrincipalBusinessActivity> Create(
        Guid id,
        string naicsCode,
        string description,
        string? sector = null
    )
    {
        if (id == Guid.Empty)
            return Result.Failure<PrincipalBusinessActivity>(new Error("Naics.Id", "Id is required."));
        if (string.IsNullOrWhiteSpace(naicsCode) || naicsCode.Length is < 2 or > 6)
            return Result.Failure<PrincipalBusinessActivity>(new Error("Naics.Code", "NAICS code must be 2-6 digits."));
        if (string.IsNullOrWhiteSpace(description) || description.Length > 300)
            return Result.Failure<PrincipalBusinessActivity>(
                new Error("Naics.Description", "Description is required and <= 300 chars.")
            );

        return Result.Success(
            new PrincipalBusinessActivity
            {
                Id = id,
                NaicsCode = naicsCode.Trim(),
                Description = description.Trim(),
                Sector = string.IsNullOrWhiteSpace(sector) ? null : sector.Trim(),
                IsActive = true,
            }
        );
    }

    public void UpdateDescription(string newDescription)
    {
        if (string.IsNullOrWhiteSpace(newDescription))
            throw new ArgumentException("Description is required.", nameof(newDescription));
        Description = newDescription.Trim();
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
