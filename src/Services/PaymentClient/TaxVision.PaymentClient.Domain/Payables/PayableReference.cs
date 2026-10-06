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

    /// <summary>Etiqueta legible del payable para el checkout (p. ej. el NÚMERO de factura "INV-2026-00010").
    /// La aporta Billing al crear el payable; el checkout la muestra en vez del id crudo. null = sin etiqueta.</summary>
    public string? Description { get; private set; }
    public Money Amount { get; private set; } = null!;

    /// <summary>Token opaco URL-safe que viaja en el path público (<c>/invoices/{Reference}</c>).
    /// No adivinable ni enumerable — 32 bytes de RNG criptográfico, base64url sin padding.</summary>
    public string Reference { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Fecha de revocación (p. ej. la factura se anuló). Una referencia revocada NO se puede
    /// pagar: el resolver deja de emitir links de checkout nuevos. null = vigente.</summary>
    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>True si el payable fue revocado (factura anulada) → el checkout debe rechazarlo.</summary>
    public bool IsRevoked => RevokedAtUtc is not null;

    /// <summary>Fecha en que la factura quedó pagada. null = sigue cobrable.</summary>
    public DateTime? SettledAtUtc { get; private set; }

    /// <summary>
    /// True si ya se cobró. Distinto de <see cref="IsRevoked"/> a propósito: anulada y pagada son
    /// dos cosas, y al cliente hay que decirle cuál de las dos es.
    /// </summary>
    public bool IsSettled => SettledAtUtc is not null;

    private PayableReference() { }

    public static Result<PayableReference> Create(
        Guid tenantId,
        PaymentPurposeKind purposeKind,
        string externalReferenceId,
        Money amount,
        DateTime nowUtc,
        string? description = null
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
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Amount = amount,
            Reference = GenerateReference(),
            CreatedAtUtc = nowUtc,
        };
        payable.SetTenant(tenantId);
        return Result.Success(payable);
    }

    /// <summary>Actualiza el monto cobrable (p. ej. la factura emitida se editó y cambió el total). No
    /// cambia el <see cref="Reference"/> ni la identidad: la URL estable sigue siendo la misma. Rechaza
    /// montos no positivos.</summary>
    public Result UpdateAmount(Money amount)
    {
        if (amount.AmountCents <= 0)
            return Result.Failure(new Error("PayableReference.InvalidAmount", "Amount must be greater than zero."));
        Amount = amount;
        return Result.Success();
    }

    /// <summary>Fija/actualiza la etiqueta legible (número de factura) si viene una no vacía. Permite
    /// backfillear payables creados antes de tener Description.</summary>
    public void SetDescription(string? description)
    {
        if (!string.IsNullOrWhiteSpace(description))
            Description = description.Trim();
    }

    /// <summary>Revoca el payable (factura anulada). Idempotente: revocar de nuevo no hace nada.</summary>
    public void Revoke(DateTime nowUtc)
    {
        RevokedAtUtc ??= nowUtc;
    }

    /// <summary>Marca el payable como cobrado. Idempotente: el evento puede reentregarse.</summary>
    public void Settle(DateTime nowUtc)
    {
        SettledAtUtc ??= nowUtc;
    }

    private static string GenerateReference()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
