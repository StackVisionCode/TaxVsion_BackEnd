using TaxVision.Customer.Domain.Customers;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Customers.Avatars;

/// <summary>
/// Estado de la subida en curso para las respuestas: null si no hay ninguna pendiente. La moderación
/// (T-20) va aparte: "under_review" | "blocked" para <see cref="ModerationFileId"/> (la última subida).
/// </summary>
public sealed record CustomerAvatarUploadState(
    Guid? PendingAvatarFileId,
    string? UploadStatus,
    string? ModerationStatus = null,
    Guid? ModerationFileId = null
);

/// <summary>
/// Una pendiente más vieja que <see cref="CustomerEntity.PendingProfilePictureTimeout"/> se informa como
/// "failed" aunque el barrido aún no la haya descartado: CloudStorage no avisa todos los rechazos.
/// En revisión de contenido nunca vence.
/// </summary>
public static class CustomerAvatarStatus
{
    public const string Processing = "processing";
    public const string Failed = "failed";
    public const string UnderReview = "under_review";
    public const string Blocked = "blocked";

    public static CustomerAvatarUploadState Of(
        Guid? pendingFileId,
        DateTime? requestedAtUtc,
        DateTime nowUtc,
        Guid? moderationFileId = null,
        ProfilePictureModerationState? moderationState = null
    )
    {
        var moderation = moderationState switch
        {
            ProfilePictureModerationState.UnderReview => UnderReview,
            ProfilePictureModerationState.Blocked => Blocked,
            _ => null,
        };
        var moderatedFile = moderation is null ? null : moderationFileId;
        if (pendingFileId is null)
            return new CustomerAvatarUploadState(null, null, moderation, moderatedFile);

        var underReview =
            moderationState == ProfilePictureModerationState.UnderReview && moderationFileId == pendingFileId;
        var expired =
            !underReview
            && (requestedAtUtc is null || nowUtc - requestedAtUtc.Value >= CustomerEntity.PendingProfilePictureTimeout);
        return new CustomerAvatarUploadState(pendingFileId, expired ? Failed : Processing, moderation, moderatedFile);
    }
}
