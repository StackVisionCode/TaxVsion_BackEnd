namespace BuildingBlocks.Messaging.CampaignsIntegrationEvents;

/// <summary>Un agendado pausado por el retiro de quien lo disparaba, con el nombre de su campaña para el aviso.</summary>
public sealed record PausedCampaignSchedule(Guid ScheduleId, Guid CampaignId, string CampaignName);
