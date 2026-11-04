using TaxVision.Correspondence.Domain.Delivery;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Domain.Compose;

/// <summary>Destinatario (To/Cc/Bcc) de un <see cref="Draft"/>. Child, sin ciclo de vida propio — mismo criterio que <see cref="IncomingEmailRecipient"/> del lado del inbox.</summary>
public sealed class DraftRecipient
{
    public const int DisplayNameMaxLength = 200;

    private DraftRecipient() { }

    public Guid Id { get; private set; }
    public Guid DraftId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Address { get; private set; } = default!;
    public EmailRecipientType Type { get; private set; }
    public string? DisplayName { get; private set; }

    /// <summary>Último resultado de entrega conocido (Postmaster); null = enviado, sin noticias todavía.</summary>
    public DeliveryOutcome? DeliveryStatus { get; private set; }

    /// <summary>Categoría del motivo cuando no se entregó o se demoró (ver <c>DeliveryFailureCategories</c>).</summary>
    public string? DeliveryCategory { get; private set; }
    public DateTime? DeliveryUpdatedAtUtc { get; private set; }

    /// <summary>
    /// Aplica un resultado. Un evento más viejo que el último aplicado se ignora (llegan fuera de orden). Devuelve
    /// <c>true</c> si cambió algo.
    /// </summary>
    internal bool RecordDelivery(DeliveryOutcome outcome, string? category, DateTime occurredAtUtc)
    {
        if (DeliveryUpdatedAtUtc is { } last && occurredAtUtc < last)
            return false;
        if (DeliveryStatus == outcome && DeliveryCategory == category && DeliveryUpdatedAtUtc == occurredAtUtc)
            return false;

        DeliveryStatus = outcome;
        DeliveryCategory = outcome == DeliveryOutcome.Delivered ? null : category;
        DeliveryUpdatedAtUtc = occurredAtUtc;
        return true;
    }

    /// <summary>Solo <see cref="Draft.AutoSave"/> construye instancias — un recipient no tiene sentido de negocio fuera de un draft.</summary>
    internal static DraftRecipient Create(
        Guid tenantId,
        Guid draftId,
        EmailAddress address,
        EmailRecipientType type,
        string? displayName
    )
    {
        ArgumentNullException.ThrowIfNull(address);

        return new DraftRecipient
        {
            Id = Guid.NewGuid(),
            DraftId = draftId,
            TenantId = tenantId,
            Address = address.NormalizedValue,
            Type = type,
            DisplayName = displayName,
        };
    }
}
