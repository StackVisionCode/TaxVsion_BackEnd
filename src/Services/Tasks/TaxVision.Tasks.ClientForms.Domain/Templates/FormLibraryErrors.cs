using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Templates;

public static class FormLibraryErrors
{
    public static readonly Error NotFound = new("FormLibrary.NotFound", "The library template was not found.");
    public static readonly Error PlatformOnly = new(
        "FormLibrary.PlatformOnly",
        "Only the platform tenant can manage the form library."
    );
    public static readonly Error ReviewVersionMismatch = new(
        "FormLibrary.ReviewVersionMismatch",
        "Only the latest published version can be reviewed."
    );
    public static readonly Error NotPublished = new(
        "FormLibrary.NotPublished",
        "The template has no published version yet."
    );
    public static readonly Error InvalidKey = new("FormLibrary.InvalidKey", "The library key is invalid.");
    public static readonly Error InvalidReviewerRole = new(
        "FormLibrary.InvalidReviewerRole",
        "The reviewer role is invalid."
    );
    public static readonly Error NoteTooLong = new("FormLibrary.NoteTooLong", "The review note is too long.");
}
