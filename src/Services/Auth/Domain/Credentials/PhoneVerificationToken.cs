using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Credentials;

/// <summary>Código OTP de 6 dígitos (hasheado) para verificar un número de teléfono.</summary>
public sealed class PhoneVerificationToken : TenantEntity
{
    public const int MaxAttempts = 5;

    private PhoneVerificationToken() { }

    public Guid UserId { get; private set; }
    public string PhoneNumber { get; private set; } = default!;
    public string CodeHash { get; private set; } = default!;
    public int Attempts { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }

    public bool IsUsable(DateTime utcNow) =>
        UsedAtUtc is null && utcNow < ExpiresAtUtc && Attempts < MaxAttempts;

    public static PhoneVerificationToken Create(
        Guid tenantId,
        Guid userId,
        string phoneNumber,
        string codeHash,
        TimeSpan validity)
    {
        var token = new PhoneVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PhoneNumber = phoneNumber.Trim(),
            CodeHash = codeHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(validity)
        };
        token.SetTenant(tenantId);
        return token;
    }

    public void RegisterAttempt() => Attempts++;

    public void MarkUsed() => UsedAtUtc ??= DateTime.UtcNow;
}
