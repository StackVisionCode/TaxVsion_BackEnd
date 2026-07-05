using TaxVision.Notification.Domain.Emailing.Campaigns;

namespace TaxVision.Notification.Application.Email.Campaigns;

public sealed record CampaignRecipientInput(string Address, string? Name = null, Dictionary<string, string?>? Variables = null);

public sealed record EmailCampaignResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string Type,
    string Status,
    Guid TemplateId,
    Guid? TemplateVersionId,
    DateTime? ScheduledAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    int TotalRecipients,
    int SentCount,
    int FailedCount,
    int OpenedCount,
    int ClickedCount,
    DateTime CreatedAtUtc
);

public static class EmailCampaignMapper
{
    public static EmailCampaignResponse ToResponse(EmailCampaign c) =>
        new(
            c.Id,
            c.TenantId,
            c.Name,
            c.Type.ToString(),
            c.Status.ToString(),
            c.TemplateId,
            c.TemplateVersionId,
            c.ScheduledAtUtc,
            c.StartedAtUtc,
            c.FinishedAtUtc,
            c.TotalRecipients,
            c.SentCount,
            c.FailedCount,
            c.OpenedCount,
            c.ClickedCount,
            c.CreatedAtUtc
        );
}
