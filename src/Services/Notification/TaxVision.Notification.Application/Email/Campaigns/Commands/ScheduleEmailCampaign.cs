using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Templates;
using Wolverine;

namespace TaxVision.Notification.Application.Email.Campaigns.Commands;

/// <summary>
/// Programa una campaña: captura la fuente de la plantilla y el layout desde CloudStorage (en el request,
/// con el token del usuario) para que el fan-out en background renderice sin CloudStorage. El scheduler
/// iniciará el envío cuando llegue la hora.
/// </summary>
public sealed record ScheduleEmailCampaignCommand(Guid CampaignId, Guid TenantId, DateTime? ScheduledAtUtc);

public static class ScheduleEmailCampaignHandler
{
    public static async Task<Result<EmailCampaignResponse>> Handle(
        ScheduleEmailCampaignCommand command,
        IEmailCampaignRepository campaigns,
        IEmailTemplateRepository templates,
        ITemplateStorageService templateStorage,
        IEmailLayoutRepository layouts,
        ILayoutStorageService layoutStorage,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(command.CampaignId, command.TenantId, ct);
        if (campaign is null)
            return Result.Failure<EmailCampaignResponse>(new Error("Campaign.NotFound", "Campaign not found."));

        var template = await templates.GetByIdAsync(campaign.TemplateId, command.TenantId, ct);
        if (template is null)
            return Result.Failure<EmailCampaignResponse>(new Error("EmailTemplate.NotFound", "Template not found."));

        if (template.Status != EmailTemplateStatus.Active || template.CurrentVersionId is null)
            return Result.Failure<EmailCampaignResponse>(
                new Error("EmailTemplate.NotPublished", "Template has no published version.")
            );

        var version = await templates.GetVersionAsync(template.Id, template.CurrentVersionId.Value, ct);
        if (version is null)
            return Result.Failure<EmailCampaignResponse>(
                new Error("EmailTemplate.NotFound", "Published version not found.")
            );

        var html = await templateStorage.GetHtmlAsync(version.HtmlFileId, ct);
        if (html.IsFailure)
            return Result.Failure<EmailCampaignResponse>(html.Error);

        string? layoutHtml = null;
        var layout = await layouts.GetDefaultAsync(command.TenantId, ct);
        if (layout?.HtmlFileId is { } layoutFileId)
        {
            var layoutResult = await layoutStorage.GetHtmlAsync(layoutFileId, ct);
            if (layoutResult.IsSuccess)
                layoutHtml = layoutResult.Value;
        }

        var scheduledAt = command.ScheduledAtUtc ?? DateTime.UtcNow;
        var scheduleResult = campaign.Schedule(
            version.Id,
            version.SubjectTemplate,
            html.Value,
            layoutHtml,
            template.VariablesJson,
            scheduledAt
        );
        if (scheduleResult.IsFailure)
            return Result.Failure<EmailCampaignResponse>(scheduleResult.Error);

        await bus.PublishAsync(
            new EmailCampaignScheduledIntegrationEvent
            {
                CampaignId = campaign.Id,
                TenantId = campaign.TenantId,
                CorrelationId = correlation.CorrelationId,
                ScheduledAtUtc = scheduledAt,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailCampaignMapper.ToResponse(campaign));
    }
}
