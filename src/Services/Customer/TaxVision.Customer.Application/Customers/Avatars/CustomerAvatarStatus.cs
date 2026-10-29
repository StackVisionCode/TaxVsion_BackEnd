using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Customers.Avatars;

/// <summary>Estado de la subida en curso para las respuestas: null si no hay ninguna pendiente.</summary>
public sealed record CustomerAvatarUploadState(Guid? PendingAvatarFileId, string? UploadStatus);

/// <summary>
/// Una pendiente más vieja que <see cref="CustomerEntity.PendingProfilePictureTimeout"/> se informa como
/// "failed" aunque el barrido aún no la haya descartado: CloudStorage no avisa todos los rechazos.
/// </summary>
public static class CustomerAvatarStatus
{
    public const string Processing = "processing";
    public const string Failed = "failed";

    public static CustomerAvatarUploadState Of(Guid? pendingFileId, DateTime? requestedAtUtc, DateTime nowUtc)
    {
        if (pendingFileId is null)
            return new CustomerAvatarUploadState(null, null);

        var expired =
            requestedAtUtc is null || nowUtc - requestedAtUtc.Value >= CustomerEntity.PendingProfilePictureTimeout;
        return new CustomerAvatarUploadState(pendingFileId, expired ? Failed : Processing);
    }
}
