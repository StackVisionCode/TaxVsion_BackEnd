using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Permissions.Abstractions;
using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Application.Scheduling.Consumers;

/// <summary>Lecturas del retiro de un empleado: el sucesor elegible y el aviso de lo que quedó pausado.</summary>
public sealed class ScheduleOffboardingResolver(
    ICampaignRepository campaigns,
    IUserPermissionsProjectionRepository permissions
)
{
    // Elegible = activo en este tenant y con permiso de programar envíos (regla compartida con Resume).
    public Task<EligibleScheduleSender?> ResolveEligibleSuccessorAsync(
        UserOffboardedIntegrationEvent evt,
        CancellationToken ct
    ) =>
        evt.SuccessorUserId is not { } candidate || candidate == evt.UserId
            ? Task.FromResult<EligibleScheduleSender?>(null)
            : ScheduleSenderEligibility.ResolveAsync(permissions, evt.TenantId, candidate, ct);

    public async Task<CampaignSchedulesPausedOnOffboardingIntegrationEvent> PausedNoticeAsync(
        UserOffboardedIntegrationEvent evt,
        IReadOnlyList<CampaignSchedule> paused,
        DateTime nowUtc,
        string correlationId,
        CancellationToken ct
    )
    {
        var names = new Dictionary<Guid, string>();
        var items = new List<PausedCampaignSchedule>();
        foreach (var schedule in paused)
        {
            if (!names.TryGetValue(schedule.CampaignId, out var name))
            {
                name = (await campaigns.GetByIdAsync(evt.TenantId, schedule.CampaignId, ct))?.Name ?? string.Empty;
                names[schedule.CampaignId] = name;
            }
            items.Add(new PausedCampaignSchedule(schedule.Id, schedule.CampaignId, name));
        }

        return new CampaignSchedulesPausedOnOffboardingIntegrationEvent
        {
            TenantId = evt.TenantId,
            OffboardedUserId = evt.UserId,
            OffboardedByUserId = evt.OffboardedByUserId,
            Schedules = items,
            PausedAtUtc = nowUtc,
            CorrelationId = correlationId,
        };
    }
}
