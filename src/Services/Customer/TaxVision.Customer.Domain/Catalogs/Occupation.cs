using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.Catalogs;

public sealed class Occupation : BaseEntity
{
    private Occupation() { }

    public string Name { get; private set; } = default!;
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<Occupation> Create(Guid id, string name, int displayOrder = 0)
    {
        if (id == Guid.Empty)
            return Result.Failure<Occupation>(new Error("Occupation.Id", "Id is required."));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            return Result.Failure<Occupation>(
                new Error("Occupation.Name", "Name is required and must be <= 120 chars.")
            );

        return Result.Success(
            new Occupation
            {
                Id = id,
                Name = name.Trim(),
                DisplayOrder = displayOrder,
                IsActive = true,
            }
        );
    }

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Name is required.", nameof(newName));
        Name = newName.Trim();
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
