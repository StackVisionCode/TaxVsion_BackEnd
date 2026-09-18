using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain.Campaigns;

/// <summary>Errores de dominio centralizados del aggregate <see cref="Campaign"/>.</summary>
public static class CampaignErrors
{
    public static readonly Error TenantRequired = new("Campaign.Tenant", "TenantId is required.");
    public static readonly Error CreatedByRequired = new("Campaign.CreatedBy", "CreatedByUserId is required.");
    public static readonly Error NameRequired = new("Campaign.Name", "Name is required.");
    public static readonly Error NameTooLong = new("Campaign.NameTooLong", "Name exceeds the maximum length.");
    public static readonly Error ChannelsRequired = new("Campaign.Channels", "At least one channel is required.");
    public static readonly Error MessageRequired = new("Campaign.Message", "Message content is required.");
    public static readonly Error MessageTooLong = new("Campaign.MessageTooLong", "Message exceeds the maximum length.");
    public static readonly Error SubjectTooLong = new("Campaign.SubjectTooLong", "Subject exceeds the maximum length.");
    public static readonly Error NotDraft = new("Campaign.NotDraft", "The campaign can only be edited while in Draft.");
    public static readonly Error NotReady = new("Campaign.NotReady", "The campaign is not in a Ready state.");
    public static readonly Error Archived = new("Campaign.Archived", "The campaign is archived.");
    public static readonly Error InvalidTransition = new("Campaign.InvalidTransition", "Invalid state transition.");
    public static readonly Error NotFound = new("Campaign.NotFound", "Campaign not found.");
}
