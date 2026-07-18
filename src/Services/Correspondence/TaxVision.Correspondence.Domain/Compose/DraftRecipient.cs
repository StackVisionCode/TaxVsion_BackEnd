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
