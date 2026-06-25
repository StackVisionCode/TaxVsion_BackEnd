using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Tenants;

public sealed class Tenant : BaseEntity
{
    private Tenant() { }

    public string Name { get; private set; } = default!;
    public string SubDomain { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<Tenant> Register(Guid id, string name, string subDomain)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<Tenant>(
                new Error("Tenant.Id", "Tenant id is required."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Tenant>(
                new Error("Tenant.Name", "Tenant name is required."));
        }

        if (string.IsNullOrWhiteSpace(subDomain))
        {
            return Result.Failure<Tenant>(
                new Error("Tenant.SubDomain", "Tenant subdomain is required."));
        }

        return Result.Success(new Tenant
        {
            Id = id,
            Name = name.Trim(),
            SubDomain = subDomain.Trim().ToLowerInvariant(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    public void UpdateFromCreatedEvent(string name, string subDomain)
    {
        Name = name.Trim();
        SubDomain = subDomain.Trim().ToLowerInvariant();
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;
}
