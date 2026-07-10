using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Domain.Emailing.Sending;

/// <summary>
/// Correo saliente con su estado, tracking y destinatarios. El cuerpo se guarda ya renderizado
/// (el render de plantillas/layout ocurre en el request, con el token del usuario); el consumer async
/// solo hace el envío. Siempre pertenece a un tenant.
/// </summary>
public sealed class OutboundEmailMessage : TenantEntity
{
    private readonly List<EmailRecipient> _recipients = [];
    private readonly List<EmailDeliveryLog> _deliveryLogs = [];

    private OutboundEmailMessage() { }

    public string Subject { get; private set; } = default!;
    public string HtmlBody { get; private set; } = default!;
    public string TextBody { get; private set; } = default!;

    // Referencias de trazabilidad (no se usan para enviar; el cuerpo ya viene renderizado).
    public Guid? TemplateId { get; private set; }
    public Guid? TemplateVersionId { get; private set; }
    public Guid? CampaignId { get; private set; }

    public EmailStatus Status { get; private set; }
    public EmailPriority Priority { get; private set; }

    public Guid? ConfigurationId { get; private set; }
    public string? ProviderType { get; private set; }

    /// <summary>Referencias a adjuntos en CloudStorage (array JSON de FileIds).</summary>
    public string AttachmentFileIdsJson { get; private set; } = "[]";

    public int RetryCount { get; private set; }
    public int MaxRetries { get; private set; }
    public string? Error { get; private set; }
    public string? CorrelationId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }
    public DateTime? OpenedAtUtc { get; private set; }
    public DateTime? ClickedAtUtc { get; private set; }
    public DateTime? BouncedAtUtc { get; private set; }

    public IReadOnlyCollection<EmailRecipient> Recipients => _recipients.AsReadOnly();
    public IReadOnlyCollection<EmailDeliveryLog> DeliveryLogs => _deliveryLogs.AsReadOnly();

    public static Result<OutboundEmailMessage> Create(
        Guid tenantId,
        string subject,
        string htmlBody,
        string textBody,
        EmailPriority priority,
        IReadOnlyList<(string Address, EmailRecipientKind Kind, string? Name)> recipients,
        string attachmentFileIdsJson,
        Guid? templateId,
        Guid? templateVersionId,
        Guid? campaignId,
        string? correlationId,
        int maxRetries = 3
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<OutboundEmailMessage>(new Error("Email.Tenant", "Tenant is required."));

        if (string.IsNullOrWhiteSpace(subject))
            return Result.Failure<OutboundEmailMessage>(new Error("Email.Subject", "Subject is required."));

        if (string.IsNullOrWhiteSpace(htmlBody))
            return Result.Failure<OutboundEmailMessage>(new Error("Email.Body", "HTML body is required."));

        var tos = recipients.Where(r => r.Kind == EmailRecipientKind.To).ToList();
        if (tos.Count == 0)
            return Result.Failure<OutboundEmailMessage>(
                new Error("Email.Recipients", "At least one 'To' recipient is required.")
            );

        foreach (var r in recipients)
            if (string.IsNullOrWhiteSpace(r.Address) || !r.Address.Contains('@'))
                return Result.Failure<OutboundEmailMessage>(
                    new Error("Email.Recipients", $"Invalid recipient address: {r.Address}.")
                );

        var message = new OutboundEmailMessage
        {
            Id = Guid.NewGuid(),
            Subject = subject.Trim(),
            HtmlBody = htmlBody,
            TextBody = textBody ?? string.Empty,
            Priority = priority,
            Status = EmailStatus.Queued,
            AttachmentFileIdsJson = string.IsNullOrWhiteSpace(attachmentFileIdsJson) ? "[]" : attachmentFileIdsJson,
            TemplateId = templateId,
            TemplateVersionId = templateVersionId,
            CampaignId = campaignId,
            CorrelationId = correlationId is { Length: > 128 } ? correlationId[..128] : correlationId,
            MaxRetries = maxRetries,
            CreatedAtUtc = DateTime.UtcNow,
        };
        message.SetTenant(tenantId);
        foreach (var r in recipients)
            message._recipients.Add(EmailRecipient.Create(r.Address, r.Kind, r.Name));

        return Result.Success(message);
    }

    public void MarkSending()
    {
        Status = EmailStatus.Sending;
        _deliveryLogs.Add(EmailDeliveryLog.Create(EmailStatus.Sending, null));
    }

    public void MarkSent(string providerType, Guid? configurationId)
    {
        Status = EmailStatus.Sent;
        ProviderType = providerType;
        ConfigurationId = configurationId;
        SentAtUtc = DateTime.UtcNow;
        Error = null;
        _deliveryLogs.Add(EmailDeliveryLog.Create(EmailStatus.Sent, providerType));
    }

    public void MarkFailed(string error)
    {
        Status = EmailStatus.Failed;
        Error = error is { Length: > 1024 } ? error[..1024] : error;
        RetryCount++;
        _deliveryLogs.Add(EmailDeliveryLog.Create(EmailStatus.Failed, error));
    }

    public void MarkBounced(string? detail)
    {
        Status = EmailStatus.Bounced;
        BouncedAtUtc = DateTime.UtcNow;
        _deliveryLogs.Add(EmailDeliveryLog.Create(EmailStatus.Bounced, detail));
    }

    /// <summary>Confirma la entrega (webhook del proveedor). Solo avanza desde Sent.</summary>
    public void MarkDelivered()
    {
        DeliveredAtUtc ??= DateTime.UtcNow;
        if (Status == EmailStatus.Sent)
            Status = EmailStatus.Delivered;
    }

    /// <summary>True si el open/click se registro por primera vez (para no duplicar contadores de campana).</summary>
    public bool MarkOpened()
    {
        if (OpenedAtUtc is not null)
            return false;
        OpenedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkClicked()
    {
        if (ClickedAtUtc is not null)
            return false;
        ClickedAtUtc = DateTime.UtcNow;
        return true;
    }

    /// <summary>True si el mensaje está en un estado desde el que puede (re)intentarse el envío.</summary>
    public bool CanDeliver() => (Status is EmailStatus.Queued or EmailStatus.Failed) && RetryCount < MaxRetries;
}
