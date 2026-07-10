using TaxVision.Notification.Domain.Emailing.Sending;

namespace TaxVision.Notification.Application.Email.Sending;

/// <summary>Destinatario en la petición de envío.</summary>
public sealed record EmailRecipientInput(
    string Address,
    EmailRecipientKind Kind = EmailRecipientKind.To,
    string? Name = null
);

public sealed record EmailRecipientSummary(string Address, string Kind, string? Name);

/// <summary>Proyección de un correo saliente (sin el cuerpo, para no exponer contenido en listados).</summary>
public sealed record OutboundEmailResponse(
    Guid Id,
    string Subject,
    string Status,
    string Priority,
    string? ProviderType,
    int RetryCount,
    string? Error,
    Guid? TemplateId,
    Guid? CampaignId,
    IReadOnlyList<EmailRecipientSummary> Recipients,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? OpenedAtUtc,
    DateTime? ClickedAtUtc,
    DateTime? BouncedAtUtc
);

public static class OutboundEmailMapper
{
    public static OutboundEmailResponse ToResponse(OutboundEmailMessage m) =>
        new(
            m.Id,
            m.Subject,
            m.Status.ToString(),
            m.Priority.ToString(),
            m.ProviderType,
            m.RetryCount,
            m.Error,
            m.TemplateId,
            m.CampaignId,
            m.Recipients.Select(r => new EmailRecipientSummary(r.Address, r.Kind.ToString(), r.Name)).ToList(),
            m.CreatedAtUtc,
            m.SentAtUtc,
            m.DeliveredAtUtc,
            m.OpenedAtUtc,
            m.ClickedAtUtc,
            m.BouncedAtUtc
        );
}
