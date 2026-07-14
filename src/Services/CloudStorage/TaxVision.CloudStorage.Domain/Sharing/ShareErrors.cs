using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Sharing;

public static class ShareErrors
{
    public static readonly Error NotFound = new("ShareLink.NotFound", "The share link was not found.");
    public static readonly Error InvalidMaxAccessCount = new(
        "ShareLink.InvalidMaxAccessCount",
        "MaxAccessCount must be greater than zero when provided."
    );
    public static readonly Error ExpirationInPast = new(
        "ShareLink.ExpirationInPast",
        "The expiration date must be in the future."
    );
    public static readonly Error AlreadyRevoked = new("ShareLink.AlreadyRevoked", "The share link is already revoked.");
    public static readonly Error PublicSharingDisabled = new(
        "ShareLink.PublicSharingDisabled",
        "Public share links are disabled for this tenant."
    );
    public static readonly Error ElevatedPermissionRequiresManage = new(
        "ShareLink.ElevatedPermissionRequiresManage",
        "Only an actor with cloudstorage.share.manage can grant Upload or EditMetadata on a share link."
    );
    public static readonly Error RecipientsRequired = new(
        "ShareLink.RecipientsRequired",
        "SpecificUsers and ExternalRecipients visibility require at least one recipient."
    );
    public static readonly Error RecursiveOnlyForFolders = new(
        "ShareLink.RecursiveOnlyForFolders",
        "IsRecursive and AppliesToFutureItems only apply to a Folder share link."
    );
    public static readonly Error Forbidden = new(
        "ShareLink.Forbidden",
        "The actor cannot manage share links for this resource."
    );
}
