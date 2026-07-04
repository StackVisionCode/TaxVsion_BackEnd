using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Credentials;

public sealed class PasswordResetToken : TenantEntity
{
    private PasswordResetToken() { }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public string? RequestedIp { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }

    public bool IsUsable(DateTime utcNow) => UsedAtUtc is null && utcNow < ExpiresAtUtc;

    public static PasswordResetToken Create(
        Guid tenantId,
        Guid userId,
        string tokenHash,
        string? requestedIp,
        TimeSpan validity
    )
    {
        var token = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            RequestedIp = requestedIp is { Length: > 45 } ? requestedIp[..45] : requestedIp,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(validity),
        };
        token.SetTenant(tenantId);
        return token;
    }

    public void MarkUsed() => UsedAtUtc ??= DateTime.UtcNow;
}
