using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.RefreshTokens;

public sealed class RefreshToken : TenantEntity
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }

    /// <summary>Sesión (familia de rotación) a la que pertenece el token. Null solo para tokens heredados pre-sesiones, que se consideran inválidos.</summary>
    public Guid? SessionId { get; private set; }

    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public string? RevokedReason { get; private set; }

    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;
    public bool WasRotated => ReplacedByTokenId is not null;

    public static RefreshToken Create(
        Guid tenantId,
        Guid userId,
        Guid sessionId,
        string tokenHash,
        DateTime expiresAtUtc
    )
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionId = sessionId,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
        };
        token.SetTenant(tenantId);
        return token;
    }

    public void Rotate(Guid replacementTokenId)
    {
        RevokedAtUtc ??= DateTime.UtcNow;
        ReplacedByTokenId = replacementTokenId;
        RevokedReason ??= "rotated";
    }

    public void Revoke(string reason)
    {
        RevokedAtUtc ??= DateTime.UtcNow;
        RevokedReason ??= reason;
    }
}
