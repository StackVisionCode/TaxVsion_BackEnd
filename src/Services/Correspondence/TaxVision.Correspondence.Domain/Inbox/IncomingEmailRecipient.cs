using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>Destinatario (To/Cc/Bcc) de un <see cref="IncomingEmail"/>. Child, sin ciclo de vida propio.</summary>
public sealed class IncomingEmailRecipient
{
    public const int DisplayNameMaxLength = 200;

    private IncomingEmailRecipient() { }

    public Guid Id { get; private set; }
    public Guid IncomingEmailId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Address { get; private set; } = default!;
    public EmailRecipientType Type { get; private set; }
    public string? DisplayName { get; private set; }

    /// <summary>
    /// Solo <see cref="IncomingEmail.Create"/> construye instancias — un recipient no tiene
    /// sentido de negocio fuera de un correo, por eso el factory es <c>internal</c> en vez de
    /// público.
    /// </summary>
    internal static IncomingEmailRecipient Create(
        Guid tenantId,
        Guid incomingEmailId,
        EmailAddress address,
        EmailRecipientType type,
        string? displayName
    )
    {
        ArgumentNullException.ThrowIfNull(address);

        return new IncomingEmailRecipient
        {
            Id = Guid.NewGuid(),
            IncomingEmailId = incomingEmailId,
            TenantId = tenantId,
            Address = address.NormalizedValue,
            Type = type,
            DisplayName = displayName,
        };
    }
}
