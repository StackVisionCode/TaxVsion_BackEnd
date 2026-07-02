using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Mfa;

public enum MfaMethodType
{
    Totp,
    Email,
    Sms
}

public sealed class MfaMethod : TenantEntity
{
    private MfaMethod() { }

    public Guid UserId { get; private set; }
    public MfaMethodType Type { get; private set; }

    /// <summary>Secreto TOTP cifrado (AES-GCM). Nunca se persiste en claro.</summary>
    public string? SecretCiphertext { get; private set; }

    /// <summary>Email o teléfono destino para OTP (según el tipo).</summary>
    public string? Destination { get; private set; }

    public bool IsConfirmed { get; private set; }
    public bool IsPreferred { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? LastUsedAtUtc { get; private set; }

    public static Result<MfaMethod> Create(
        Guid tenantId,
        Guid userId,
        MfaMethodType type,
        string? secretCiphertext,
        string? destination)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            return Result.Failure<MfaMethod>(new Error("Mfa.Scope", "Tenant and user are required."));

        if (type == MfaMethodType.Totp && string.IsNullOrWhiteSpace(secretCiphertext))
            return Result.Failure<MfaMethod>(new Error("Mfa.Secret", "TOTP secret is required."));

        if (type is MfaMethodType.Email or MfaMethodType.Sms &&
            string.IsNullOrWhiteSpace(destination))
        {
            return Result.Failure<MfaMethod>(
                new Error("Mfa.Destination", "Destination is required for OTP methods."));
        }

        var method = new MfaMethod
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            SecretCiphertext = secretCiphertext,
            Destination = destination?.Trim(),
            IsConfirmed = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        method.SetTenant(tenantId);
        return Result.Success(method);
    }

    public void Confirm() => IsConfirmed = true;

    public void MarkPreferred(bool preferred) => IsPreferred = preferred;

    public void MarkUsed() => LastUsedAtUtc = DateTime.UtcNow;

    public void ReplaceSecret(string secretCiphertext)
    {
        SecretCiphertext = secretCiphertext;
        IsConfirmed = false;
    }
}
