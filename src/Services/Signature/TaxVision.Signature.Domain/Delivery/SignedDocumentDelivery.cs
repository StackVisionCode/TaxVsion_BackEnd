using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Delivery;

/// <summary>
/// Entrega de documentos firmados a UN firmante: qué archivos, a qué email, hasta cuándo vale el enlace y
/// cuántas veces se abrió. Es parte del proceso de firma, no de la gestión documental: no crea ningún
/// share en CloudStorage. El enlace lleva un secreto del que solo se guarda el hash; reemitir lo rota
/// (el anterior deja de valer) y siempre va al mismo email.
/// </summary>
public sealed class SignedDocumentDelivery : TenantEntity
{
    public const int MaxEmailLength = 320;

    /// <summary>Reemisión desde la página pública: no más de una cada este intervalo (anti abuso).</summary>
    public static readonly TimeSpan PublicReissueCooldown = TimeSpan.FromMinutes(5);

    private readonly List<DeliveryItem> _items = [];

    private SignedDocumentDelivery() { }

    public Guid SignatureRequestId { get; private set; }
    public Guid SignerId { get; private set; }
    public string RecipientEmail { get; private set; } = default!;
    public DeliveryPurpose Purpose { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastIssuedAtUtc { get; private set; }
    public int IssueCount { get; private set; }
    public int AccessCount { get; private set; }
    public DateTime? LastAccessedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public Guid? RevokedByUserId { get; private set; }

    /// <summary>Archivos de la entrega; el orden de presentación es <see cref="DeliveryItem.Order"/>.</summary>
    public IReadOnlyList<DeliveryItem> Items => _items.AsReadOnly();

    public static Result<SignedDocumentDelivery> Issue(
        Guid tenantId,
        Guid signatureRequestId,
        Guid signerId,
        string recipientEmail,
        DeliveryPurpose purpose,
        IReadOnlyList<DeliveryItemSpec> items,
        DeliveryToken token,
        TimeSpan lifetime,
        DateTime nowUtc
    )
    {
        ArgumentNullException.ThrowIfNull(token);
        if (tenantId == Guid.Empty || signatureRequestId == Guid.Empty || signerId == Guid.Empty)
            return Result.Failure<SignedDocumentDelivery>(
                new Error("Signature.Delivery.Identity", "Tenant, request and signer are required.")
            );
        if (string.IsNullOrWhiteSpace(recipientEmail) || recipientEmail.Trim().Length > MaxEmailLength)
            return Result.Failure<SignedDocumentDelivery>(
                new Error("Signature.Delivery.Email", "A recipient email is required.")
            );
        if (items is null || items.Count == 0)
            return Result.Failure<SignedDocumentDelivery>(
                new Error("Signature.Delivery.NoItems", "A delivery needs at least one file.")
            );
        if (lifetime <= TimeSpan.Zero)
            return Result.Failure<SignedDocumentDelivery>(
                new Error("Signature.Delivery.Lifetime", "The link lifetime must be positive.")
            );

        var delivery = new SignedDocumentDelivery
        {
            Id = Guid.NewGuid(),
            SignatureRequestId = signatureRequestId,
            SignerId = signerId,
            RecipientEmail = recipientEmail.Trim(),
            Purpose = purpose,
            TokenHash = token.Hash,
            ExpiresAtUtc = nowUtc.Add(lifetime),
            CreatedAtUtc = nowUtc,
            LastIssuedAtUtc = nowUtc,
            IssueCount = 1,
        };
        delivery.SetTenant(tenantId);
        for (var index = 0; index < items.Count; index++)
            delivery._items.Add(DeliveryItem.Create(delivery.Id, items[index], index + 1));
        return Result.Success(delivery);
    }

    public DeliveryStatus StatusAt(DateTime nowUtc) =>
        RevokedAtUtc is not null ? DeliveryStatus.Revoked
        : nowUtc >= ExpiresAtUtc ? DeliveryStatus.Expired
        : DeliveryStatus.Active;

    /// <summary>Nuevo secreto y nueva vigencia; el enlace anterior deja de funcionar. Una revocada no se reemite.</summary>
    public Result Reissue(DeliveryToken token, TimeSpan lifetime, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (RevokedAtUtc is not null)
            return Result.Failure(
                new Error("Signature.Delivery.Revoked", "This delivery was revoked by the office.")
            );
        if (lifetime <= TimeSpan.Zero)
            return Result.Failure(new Error("Signature.Delivery.Lifetime", "The link lifetime must be positive."));

        TokenHash = token.Hash;
        ExpiresAtUtc = nowUtc.Add(lifetime);
        LastIssuedAtUtc = nowUtc;
        IssueCount++;
        return Result.Success();
    }

    /// <summary>Reemisión pedida por quien tiene el enlace (vencido): con enfriamiento para que no se use de spam.</summary>
    public bool CanBeReissuedPubliclyAt(DateTime nowUtc) =>
        RevokedAtUtc is null && nowUtc - LastIssuedAtUtc >= PublicReissueCooldown;

    public Result RegisterAccess(DateTime nowUtc)
    {
        if (StatusAt(nowUtc) != DeliveryStatus.Active)
            return Result.Failure(new Error("Signature.Delivery.NotActive", "This link is no longer active."));

        AccessCount++;
        LastAccessedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Revoke(Guid byUserId, DateTime nowUtc)
    {
        if (RevokedAtUtc is not null)
            return Result.Success();

        RevokedAtUtc = nowUtc;
        RevokedByUserId = byUserId;
        return Result.Success();
    }
}
