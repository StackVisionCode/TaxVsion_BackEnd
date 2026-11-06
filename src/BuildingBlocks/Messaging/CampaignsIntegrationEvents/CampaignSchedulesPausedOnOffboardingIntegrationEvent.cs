using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CampaignsIntegrationEvents;

/// <summary>
/// <c>campaign.schedules_paused_on_offboarding.v1</c> — se retiró a quien disparaba estos agendados y no
/// había sucesor elegible (activo y con <c>campaigns.send</c>), así que Campaigns los pausó para que no
/// sigan enviando en su nombre. Notification avisa a quienes administran campañas para que decidan qué
/// hacer con ellos (cancelarlos o volver a agendarlos). Un evento por retiro, con todos los agendados.
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.CampaignsIntegrationEvents.CampaignSchedulesPausedOnOffboardingIntegrationEvent")]
public sealed record CampaignSchedulesPausedOnOffboardingIntegrationEvent : IntegrationEvent
{
    /// <summary>Empleado retirado en cuyo nombre disparaban.</summary>
    public required Guid OffboardedUserId { get; init; }

    /// <summary>Quién ejecutó el retiro; null = sistema.</summary>
    public Guid? OffboardedByUserId { get; init; }

    public required IReadOnlyList<PausedCampaignSchedule> Schedules { get; init; }

    public required DateTime PausedAtUtc { get; init; }
}
