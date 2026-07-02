using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Credentials;

/// <summary>Token de verificación para cambio de email. Se envía a la dirección nueva.</summary>
public sealed class EmailVerificationToken : TenantEntity
{
    private EmailVerificationToken() { }

    public Guid UserId { get; private set; }
    public string NewEmail { get; private set; } = default!;
    public string TokenHash { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }

    public bool IsUsable(DateTime utcNow) => UsedAtUtc is null && utcNow < ExpiresAtUtc;

    public static EmailVerificationToken Create(
        Guid tenantId,
        Guid userId,
        string newEmail,
        string tokenHash,
        TimeSpan validity)
    {
        var token = new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NewEmail = newEmail.Trim().ToLowerInvariant(),
            TokenHash = tokenHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(validity)
        };
        token.SetTenant(tenantId);
        return token;
    }

    public void MarkUsed() => UsedAtUtc ??= DateTime.UtcNow;
}
