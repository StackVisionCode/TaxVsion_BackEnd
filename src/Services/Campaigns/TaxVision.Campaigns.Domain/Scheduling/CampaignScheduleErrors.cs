using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain.Scheduling;

/// <summary>Errores de dominio centralizados del aggregate <see cref="CampaignSchedule"/>.</summary>
public static class CampaignScheduleErrors
{
    public static readonly Error TenantRequired = new("CampaignSchedule.Tenant", "TenantId is required.");
    public static readonly Error CampaignRequired = new("CampaignSchedule.Campaign", "CampaignId is required.");
    public static readonly Error RunAtRequired = new("CampaignSchedule.RunAt", "A run time (UTC) is required.");
    public static readonly Error IntervalInvalid = new(
        "CampaignSchedule.Interval",
        "A recurring schedule needs a positive interval in minutes."
    );
    public static readonly Error NotActive = new("CampaignSchedule.NotActive", "The schedule is not active.");
    public static readonly Error NotPaused = new("CampaignSchedule.NotPaused", "The schedule is not paused.");
    public static readonly Error InvalidTransition = new(
        "CampaignSchedule.InvalidTransition",
        "Invalid state transition."
    );
    public static readonly Error NotFound = new("CampaignSchedule.NotFound", "Schedule not found.");
}
