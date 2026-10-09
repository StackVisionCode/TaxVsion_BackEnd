using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain.Templates;

/// <summary>Errores de dominio del aggregate <see cref="CampaignTemplate"/>.</summary>
public static class CampaignTemplateErrors
{
    public static readonly Error TenantRequired = new("CampaignTemplate.Tenant", "TenantId is required.");
    public static readonly Error CreatedByRequired = new("CampaignTemplate.CreatedBy", "CreatedByUserId is required.");
    public static readonly Error NameRequired = new("CampaignTemplate.Name", "Name is required.");
    public static readonly Error NameTooLong = new("CampaignTemplate.NameTooLong", "Name exceeds the maximum length.");
    public static readonly Error DescriptionTooLong = new(
        "CampaignTemplate.DescriptionTooLong",
        "Description exceeds the maximum length."
    );
    public static readonly Error ChannelsRequired = new(
        "CampaignTemplate.Channels",
        "At least one channel is required."
    );
    public static readonly Error ContentChannelInvalid = new(
        "CampaignTemplate.ContentChannelInvalid",
        "Content must target exactly one channel."
    );
    public static readonly Error ContentChannelNotSelected = new(
        "CampaignTemplate.ContentChannelNotSelected",
        "The content's channel is not one of the template's channels."
    );
    public static readonly Error BodyRequired = new("CampaignTemplate.Body", "Content body is required.");
    public static readonly Error BodyTooLong = new("CampaignTemplate.BodyTooLong", "Body exceeds the maximum length.");
    public static readonly Error SubjectTooLong = new(
        "CampaignTemplate.SubjectTooLong",
        "Subject exceeds the maximum length."
    );
    public static readonly Error TitleTooLong = new(
        "CampaignTemplate.TitleTooLong",
        "Title exceeds the maximum length."
    );
    public static readonly Error NotFound = new("CampaignTemplate.NotFound", "Template not found.");
}
