using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Application.Scheduling.Consumers;

/// <summary>
/// Libera el guard de solape (overlap=skip) de un schedule cuando su run termina: consume
/// <c>campaign.run.completed.v1</c> (que la propia Campaigns publica) y limpia <c>ActiveRunId</c> del
/// schedule dueño de ese run, dejándolo elegible para el próximo disparo. No-op si el run no vino de un
/// schedule (envío manual/audiencia). SIN dinero.
/// </summary>
public static class ReleaseScheduleOnRunCompletedConsumer
{
    public static async Task Handle(
        CampaignRunCompletedIntegrationEvent evt,
        ICampaignScheduleRepository schedules,
        ICampaignRunRepository runs,
        IUnitOfWork unitOfWork,
        ILogger<CampaignSchedule> logger,
        CancellationToken ct
    )
    {
        var schedule = await schedules.GetByActiveRunIdAsync(evt.TenantId, evt.RunId, ct);
        if (schedule is null)
        {
            // Defensa: buscar el agendado por el ScheduleId del run.
            var scheduleId = await runs.GetScheduleIdAsync(evt.TenantId, evt.RunId, ct);
            if (scheduleId is null)
            {
                logger.LogDebug("Run {RunId} has no schedule (manual send); nothing to release.", evt.RunId);
                return;
            }
            schedule = await schedules.GetByIdAsync(evt.TenantId, scheduleId.Value, ct);
            if (schedule is null)
                return;
        }

        schedule.ReleaseActiveRun(evt.RunId);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
