using System.Security.Cryptography;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.Payables;

/// <summary>
/// Ancla ESTABLE y provider-neutral de algo cobrable del tenant (p. ej. una factura). A diferencia
/// de un <see cref="PaymentLinks.PaymentLink"/> —que lleva un token que expira— la referencia no
/// caduca: es la que se embebe en un PDF que vive años. El resolver público la traduce, al abrirse,
/// al link de checkout vigente (creando uno nuevo si el anterior expiró). Idempotente por
/// (TenantId, PurposeKind, ExternalReferenceId) — un mismo payable no se duplica. El propósito se
/// guarda aplanado (no como VO owned) para poder indexarlo junto a TenantId.
/// </summary>
public sealed class PayableReference : TenantEntity
{
    public PaymentPurposeKind PurposeKind { get; private set; }
    public string ExternalReferenceId { get; private set; } = string.Empty;
    public Money Amount { get; private set; } = null!;

    /// <summary>Token opaco URL-safe que viaja en el path público (<c>/invoices/{Reference}</c>).
    /// No adivinable ni enumerable — 32 bytes de RNG criptográfico, base64url sin padding.</summary>
    public string Reference { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    private PayableReference() { }

    public static Result<PayableReference> Create(
        Guid tenantId,
        PaymentPurposeKind purposeKind,
        string externalReferenceId,
        Money amount,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<PayableReference>(
                new Error("PayableReference.InvalidTenant", "TenantId is required.")
            );
        if (string.IsNullOrWhiteSpace(externalReferenceId))
            return Result.Failure<PayableReference>(
                new Error("PayableReference.InvalidReference", "ExternalReferenceId is required.")
            );
        if (externalReferenceId.Length > 200)
            return Result.Failure<PayableReference>(
                new Error("PayableReference.ReferenceTooLong", "ExternalReferenceId must be 200 characters or fewer.")
            );
        if (amount.AmountCents <= 0)
            return Result.Failure<PayableReference>(
                new Error("PayableReference.InvalidAmount", "Amount must be greater than zero.")
            );

        var payable = new PayableReference
        {
            PurposeKind = purposeKind,
            ExternalReferenceId = externalReferenceId.Trim(),
            Amount = amount,
            Reference = GenerateReference(),
            CreatedAtUtc = nowUtc,
        };
        payable.SetTenant(tenantId);
        return Result.Success(payable);
    }

    private static string GenerateReference()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
