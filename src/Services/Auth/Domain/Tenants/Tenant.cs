using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Auth.Domain.Tenants;

public sealed class Tenant : BaseEntity
{
    private Tenant() { }

    public string Name { get; private set; } = default!;
    public string SubDomain { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string? AdminEmail { get; private set; }
    public string? AdminInvitationTokenHash { get; private set; }
    public Guid? AdminUserId { get; private set; }
    public DateTime? AdminInvitationConsumedAtUtc { get; private set; }

    public static Result<Tenant> Register(
        Guid id,
        string name,
        string subDomain,
        string adminEmail,
        string adminInvitationTokenHash)
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

        if (string.IsNullOrWhiteSpace(adminEmail) ||
            string.IsNullOrWhiteSpace(adminInvitationTokenHash))
        {
            return Result.Failure<Tenant>(
                new Error("Tenant.AdminInvitation", "Admin invitation data is required."));
        }

        return Result.Success(new Tenant
        {
            Id = id,
            Name = name.Trim(),
            SubDomain = subDomain.Trim().ToLowerInvariant(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            AdminEmail = adminEmail.Trim().ToLowerInvariant(),
            AdminInvitationTokenHash = adminInvitationTokenHash
        });
    }

    public void UpdateFromCreatedEvent(
        string name,
        string subDomain,
        string adminEmail,
        string adminInvitationTokenHash)
    {
        Name = name.Trim();
        SubDomain = subDomain.Trim().ToLowerInvariant();
        IsActive = true;
        AdminEmail ??= adminEmail.Trim().ToLowerInvariant();
        AdminInvitationTokenHash ??= adminInvitationTokenHash;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void SetActive(bool isActive) => IsActive = isActive;

    public bool MatchesAdminInvitation(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(AdminInvitationTokenHash) ||
            string.IsNullOrWhiteSpace(rawToken))
        {
            return false;
        }

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var expected = Convert.FromHexString(AdminInvitationTokenHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public void MarkAdminInvitationConsumed(Guid userId)
    {
        AdminUserId ??= userId;
        AdminInvitationConsumedAtUtc ??= DateTime.UtcNow;
    }
}
