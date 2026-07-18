using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Emailing.Sending;

/// <summary>Destinatario de un correo saliente (To/Cc/Bcc).</summary>
/// <remarks>Migration target: <b>Postmaster</b> (rename a <c>SentMessageRecipient</c>). See <c>Responsibility_Map.md</c>.</remarks>
public sealed class EmailRecipient : BaseEntity
{
    private EmailRecipient() { }

    public Guid MessageId { get; private set; }
    public string Address { get; private set; } = default!;
    public EmailRecipientKind Kind { get; private set; }
    public string? Name { get; private set; }

    // Tracking por destinatario (para pixel/click/bounce vía webhook en el futuro).
    public DateTime? OpenedAtUtc { get; private set; }
    public DateTime? ClickedAtUtc { get; private set; }
    public DateTime? BouncedAtUtc { get; private set; }

    internal static EmailRecipient Create(string address, EmailRecipientKind kind, string? name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Address = address.Trim(),
            Kind = kind,
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
        };
}
