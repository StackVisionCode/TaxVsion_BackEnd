using System.Text.RegularExpressions;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.Enums;


namespace TaxVision.Tenant.Domain;


public partial class Tenant : BaseEntity
{
    public string Name { get; private set; } = default!;
    public string SubDomain { get; private set; } = default!;
    public EnumTenantStatus.TenantStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Tenant() { }

    public static Result<Tenant> Create(string name, string subdomain)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Tenant>(new Error("Tenant.Name", "Name is required."));

        var sub = (subdomain ?? "").Trim().ToLower();

        if (!MyRegex().IsMatch(sub))
            return Result.Failure<Tenant>(new Error("Tenant.Subdomain", "Subdomain must be 3-40 characters long and contain only lowercase letters and numbers (a-z, 0-9) and hyphens (-)."));

        var tenant = new Tenant

        {
            Name = name,
            SubDomain = sub,
            Status = EnumTenantStatus.TenantStatus.Active,
            CreatedAtUtc = DateTime.UtcNow

        };

        return Result.Success(tenant);
    }

    public Result Suspend()
    {
        if (Status != EnumTenantStatus.TenantStatus.Active)
            return Result.Failure(new Error("Tenant.Status", "Only active tenants can be suspended."));

        Status = EnumTenantStatus.TenantStatus.Suspended;
        return Result.Success();
    }

    public Result ChangeStatus(EnumTenantStatus.TenantStatus status)
    {
        if (Status == EnumTenantStatus.TenantStatus.Closed &&
            status != EnumTenantStatus.TenantStatus.Closed)
        {
            return Result.Failure(
                new Error("Tenant.Status", "A closed tenant cannot be reactivated."));
        }

        Status = status;
        return Result.Success();
    }

    private static Regex MyRegex()
    {
#pragma warning disable SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
        return new Regex("^[a-z0-9-]{3,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
#pragma warning restore SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
    }
}
