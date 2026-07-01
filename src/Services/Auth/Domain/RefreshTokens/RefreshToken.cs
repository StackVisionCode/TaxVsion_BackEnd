using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.RefreshTokens;

public sealed class RefreshToken : BaseEntity
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;

    public static RefreshToken Create(Guid userId, string tokenHash, DateTime expiresAtUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow
        };

    public void Revoke() => RevokedAtUtc ??= DateTime.UtcNow;
}
