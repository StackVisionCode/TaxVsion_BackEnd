using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Templates;

public static class FormTemplateErrors
{
    public static readonly Error NotFound = new("FormTemplate.NotFound", "The form template was not found.");
    public static readonly Error Archived = new("FormTemplate.Archived", "The form template is archived.");
    public static readonly Error NotArchived = new("FormTemplate.NotArchived", "The form template is not archived.");
    public static readonly Error NothingToPublish = new(
        "FormTemplate.NothingToPublish",
        "The draft has no unpublished changes."
    );
    public static readonly Error HasPublishedVersions = new(
        "FormTemplate.HasPublishedVersions",
        "A published form template cannot be deleted."
    );
    public static readonly Error InvalidDefinition = new(
        "FormTemplate.InvalidDefinition",
        "The form definition is invalid."
    );
    public static readonly Error VersionMismatch = new(
        "FormTemplate.VersionMismatch",
        "The version does not belong to this form template."
    );
    public static readonly Error ConcurrencyConflict = new(
        "FormTemplate.ConcurrencyConflict",
        "The form template was modified by someone else."
    );
    public static readonly Error InvalidName = new("FormTemplate.InvalidName", "The form template name is invalid.");
    public static readonly Error InvalidDescription = new(
        "FormTemplate.InvalidDescription",
        "The form template description is invalid."
    );
    public static readonly Error InvalidContentHash = new(
        "FormTemplate.InvalidContentHash",
        "The content hash is invalid."
    );
    public static readonly Error InvalidOwner = new(
        "FormTemplate.InvalidOwner",
        "Tenant and user identifiers are required."
    );
    public static readonly Error InvalidPurpose = new("FormTemplate.InvalidPurpose", "The form purpose is invalid.");
    public static readonly Error InvalidPresentation = new(
        "FormTemplate.InvalidPresentation",
        "The form presentation is invalid."
    );
    public static readonly Error ConfigureActionsRequired = new(
        "FormTemplate.ConfigureActionsRequired",
        "You do not have permission to change the data mapping or the actions of this form."
    );
    public static readonly Error InvalidLibraryOrigin = new(
        "FormTemplate.InvalidLibraryOrigin",
        "The library origin is invalid."
    );
}
