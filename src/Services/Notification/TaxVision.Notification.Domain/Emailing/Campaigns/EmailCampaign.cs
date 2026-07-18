using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Domain.Emailing.Campaigns;

public enum CampaignType
{
    Newsletter,
    Notification,
    Marketing,
    Custom,
}

public enum CampaignStatus
{
    Draft,
    Scheduled,
    Running,
    Paused,
    Completed,
    Cancelled,
    Failed,
}

/// <summary>
/// Campaña de correo de un tenant. Al programar se captura la fuente de la plantilla (subject/HTML) y
/// el layout desde CloudStorage (en el request, con token de usuario) para que el fan-out en background
/// renderice por destinatario sin depender de CloudStorage. El procesamiento masivo es por cola/evento.
/// </summary>
/// <remarks>Migration target: <b>Scribe</b> (junto con <see cref="EmailCampaignRecipient"/>). See <c>Responsibility_Map.md</c>. Se elimina de Notification en Fase 7.</remarks>
public sealed class EmailCampaign : TenantEntity
{
    private readonly List<EmailCampaignRecipient> _recipients = [];

    private EmailCampaign() { }

    public string Name { get; private set; } = default!;
    public CampaignType Type { get; private set; }
    public CampaignStatus Status { get; private set; }

    public Guid TemplateId { get; private set; }
    public Guid? TemplateVersionId { get; private set; }

    // Fuente capturada al programar (para render en background sin CloudStorage).
    public string? SubjectTemplate { get; private set; }
    public string? HtmlTemplate { get; private set; }
    public string? LayoutHtml { get; private set; }
    public string AllowedVariablesJson { get; private set; } = "[]";

    public DateTime? ScheduledAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }

    public int TotalRecipients { get; private set; }
    public int SentCount { get; private set; }
    public int FailedCount { get; private set; }
    public int OpenedCount { get; private set; }
    public int ClickedCount { get; private set; }

    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<EmailCampaignRecipient> Recipients => _recipients.AsReadOnly();

    public static Result<EmailCampaign> Create(
        Guid tenantId,
        string name,
        CampaignType type,
        Guid templateId,
        IReadOnlyList<(string Address, string? Name, string? VariablesJson)> recipients,
        Guid? createdByUserId
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<EmailCampaign>(new Error("Campaign.Tenant", "Tenant is required."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<EmailCampaign>(new Error("Campaign.Name", "Campaign name is required."));

        if (templateId == Guid.Empty)
            return Result.Failure<EmailCampaign>(new Error("Campaign.Template", "A template is required."));

        if (recipients.Count == 0)
            return Result.Failure<EmailCampaign>(
                new Error("Campaign.Recipients", "At least one recipient is required.")
            );

        foreach (var r in recipients)
            if (string.IsNullOrWhiteSpace(r.Address) || !r.Address.Contains('@'))
                return Result.Failure<EmailCampaign>(
                    new Error("Campaign.Recipients", $"Invalid recipient address: {r.Address}.")
                );

        var campaign = new EmailCampaign
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Type = type,
            Status = CampaignStatus.Draft,
            TemplateId = templateId,
            TotalRecipients = recipients.Count,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        campaign.SetTenant(tenantId);
        foreach (var r in recipients)
            campaign._recipients.Add(EmailCampaignRecipient.Create(r.Address, r.Name, r.VariablesJson));

        return Result.Success(campaign);
    }

    /// <summary>Captura la plantilla/layout resueltos y programa la campaña.</summary>
    public Result Schedule(
        Guid templateVersionId,
        string subjectTemplate,
        string htmlTemplate,
        string? layoutHtml,
        string allowedVariablesJson,
        DateTime scheduledAtUtc
    )
    {
        if (Status != CampaignStatus.Draft)
            return Result.Failure(new Error("Campaign.State", "Only draft campaigns can be scheduled."));

        if (string.IsNullOrWhiteSpace(htmlTemplate))
            return Result.Failure(new Error("Campaign.Template", "Template HTML is required to schedule."));

        TemplateVersionId = templateVersionId;
        SubjectTemplate = subjectTemplate;
        HtmlTemplate = htmlTemplate;
        LayoutHtml = layoutHtml;
        AllowedVariablesJson = string.IsNullOrWhiteSpace(allowedVariablesJson) ? "[]" : allowedVariablesJson;
        ScheduledAtUtc = scheduledAtUtc;
        Status = CampaignStatus.Scheduled;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result MarkRunning()
    {
        if (Status != CampaignStatus.Scheduled)
            return Result.Failure(new Error("Campaign.State", "Only scheduled campaigns can start."));

        Status = CampaignStatus.Running;
        StartedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public void IncrementSent()
    {
        SentCount++;
        UpdatedAtUtc = DateTime.UtcNow;
        CompleteIfDone();
    }

    public void IncrementFailed()
    {
        FailedCount++;
        UpdatedAtUtc = DateTime.UtcNow;
        CompleteIfDone();
    }

    public void IncrementOpened() => OpenedCount++;

    public void IncrementClicked() => ClickedCount++;

    public Result Cancel()
    {
        if (Status is CampaignStatus.Completed or CampaignStatus.Cancelled)
            return Result.Failure(
                new Error("Campaign.State", "The campaign cannot be cancelled in its current state.")
            );

        Status = CampaignStatus.Cancelled;
        FinishedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    private void CompleteIfDone()
    {
        if (Status == CampaignStatus.Running && SentCount + FailedCount >= TotalRecipients)
        {
            Status = CampaignStatus.Completed;
            FinishedAtUtc = DateTime.UtcNow;
        }
    }
}
