using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain.Runs;

public static class CampaignRunErrors
{
    public static readonly Error TenantRequired = new("CampaignRun.Tenant", "TenantId is required.");
    public static readonly Error CampaignRequired = new("CampaignRun.Campaign", "CampaignId is required.");
    public static readonly Error NoRecipients = new(
        "CampaignRun.NoRecipients",
        "At least one recipient unit is required."
    );
    public static readonly Error NotFound = new("CampaignRun.NotFound", "Campaign run not found.");
    public static readonly Error RecipientNotFound = new("CampaignRun.RecipientNotFound", "Dispatch unit not found.");
    public static readonly Error NotDispatching = new("CampaignRun.NotDispatching", "The run is not dispatching.");
}
