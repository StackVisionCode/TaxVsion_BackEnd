using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;

namespace TaxVision.Auth.Domain.Tenants;

public sealed class Tenant : BaseEntity
{
    private Tenant() { }

    public string Name { get; private set; } = default!;
    public string SubDomain { get; private set; } = default!;
    public TenantKind Kind { get; private set; }
    public string DefaultTimeZoneId { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<Tenant> Register(
        Guid id,
        string name,
        string subDomain,
        TenantKind kind,
        string defaultTimeZoneId
    )
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<Tenant>(new Error("Tenant.Id", "Tenant id is required."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Tenant>(new Error("Tenant.Name", "Tenant name is required."));
        }

        if (string.IsNullOrWhiteSpace(subDomain))
        {
            return Result.Failure<Tenant>(new Error("Tenant.SubDomain", "Tenant subdomain is required."));
        }

        if (!IanaTimeZone.TryNormalize(defaultTimeZoneId, out var normalizedTimeZoneId))
        {
            return Result.Failure<Tenant>(
                new Error("Tenant.DefaultTimeZoneId", "Tenant default time zone must be a valid IANA identifier.")
            );
        }

        return Result.Success(
            new Tenant
            {
                Id = id,
                Name = name.Trim(),
                SubDomain = subDomain.Trim().ToLowerInvariant(),
                Kind = kind,
                DefaultTimeZoneId = normalizedTimeZoneId,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
    }

    public void UpdateFromCreatedEvent(string name, string subDomain, TenantKind kind, string defaultTimeZoneId)
    {
        Name = name.Trim();
        SubDomain = subDomain.Trim().ToLowerInvariant();
        Kind = kind;
        if (!IanaTimeZone.TryNormalize(defaultTimeZoneId, out var normalizedTimeZoneId))
        {
            throw new ArgumentException(
                "Tenant default time zone must be a valid IANA identifier.",
                nameof(defaultTimeZoneId)
            );
        }

        DefaultTimeZoneId = normalizedTimeZoneId;
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void SetActive(bool isActive) => IsActive = isActive;
}
