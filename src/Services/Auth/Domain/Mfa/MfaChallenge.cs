using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Mfa;

/// <summary>
/// Desafío MFA pendiente entre el paso 1 (credenciales válidas) y el paso 2 (código verificado)
/// del login. El "login ticket" es un token opaco que el cliente presenta en el paso 2.
/// </summary>
public sealed class MfaChallenge : TenantEntity
{
    public const int MaxAttempts = 5;

    private MfaChallenge() { }

    public Guid UserId { get; private set; }
    public Guid? MfaMethodId { get; private set; }
    public string LoginTicketHash { get; private set; } = default!;

    /// <summary>Hash del OTP enviado (email/SMS). Null para TOTP y recovery codes.</summary>
    public string? OtpHash { get; private set; }

    public int Attempts { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }

    public bool IsUsable(DateTime utcNow) =>
        ConsumedAtUtc is null && utcNow < ExpiresAtUtc && Attempts < MaxAttempts;

    public static MfaChallenge Create(
        Guid tenantId,
        Guid userId,
        Guid? mfaMethodId,
        string loginTicketHash,
        string? otpHash,
        TimeSpan validity)
    {
        var challenge = new MfaChallenge
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MfaMethodId = mfaMethodId,
            LoginTicketHash = loginTicketHash,
            OtpHash = otpHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(validity)
        };
        challenge.SetTenant(tenantId);
        return challenge;
    }

    public void RegisterAttempt() => Attempts++;

    public void Consume() => ConsumedAtUtc = DateTime.UtcNow;
}
