using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Runs;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Audience;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using Wolverine;

namespace TaxVision.Campaigns.Application.Scheduling.Commands;

/// <summary>El run del slot: el que ya existe (un intento anterior no terminó) o uno nuevo con la audiencia de ahora.</summary>
public sealed class ScheduledRunOpener(
    ICampaignRepository campaigns,
    ICampaignRunRepository runs,
    CampaignAudienceService audience
)
{
    // Actor de sistema cuando el agendado no tiene creador.
    private static readonly Guid SchedulerActor = new("5c4ed011-0000-0000-0000-0000005c4ed0");

    public async Task<Result<OpenedCampaignRun>> OpenAsync(
        CampaignSchedule schedule,
        ScheduledSlot slot,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(schedule.TenantId, schedule.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<OpenedCampaignRun>(CampaignErrors.NotFound);
        if (campaign.Status == CampaignStatus.Archived)
            return Result.Failure<OpenedCampaignRun>(CampaignErrors.Archived);

        var existing = await runs.GetByIdAsync(schedule.TenantId, ScheduledRunId.For(schedule.Id, slot.SlotUtc), ct);
        if (existing is not null)
            return existing.Status == CampaignRunStatus.Created
                ? Result.Success(OpenedCampaignRun.From(existing))
                : Result.Failure<OpenedCampaignRun>(CampaignRunErrors.NotCreated);

        // A1 — dispara en nombre de quien agendó (o del sucesor) y con SU visibilidad de clientes.
        var actor = schedule.EffectiveRunAsUserId ?? SchedulerActor;
        var resolved = await audience.ResolveAsync(
            campaign,
            new CampaignAudienceRequest(
                actor,
                schedule.ContactListIds(),
                [],
                schedule.IncludeCustomers,
                schedule.CustomerIds(),
                schedule.CreatorCanViewAllCustomers
            ),
            ct
        );
        if (resolved.IsFailure)
            return Result.Failure<OpenedCampaignRun>(resolved.Error);

        return await bus.InvokeAsync<Result<OpenedCampaignRun>>(
            new OpenCampaignRunCommand(schedule.TenantId, campaign.Id, actor, "Scheduled", resolved.Value, slot),
            ct
        );
    }
}
