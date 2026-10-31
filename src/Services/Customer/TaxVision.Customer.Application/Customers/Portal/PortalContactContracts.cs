namespace TaxVision.Customer.Application.Customers.Portal;

// Autoservicio del cliente del portal sobre SU ficha: el CustomerId sale siempre del token.

public sealed record GetMyCustomerContactQuery(Guid TenantId, Guid CustomerId);

/// <param name="AvatarFileId">Foto vigente (null = iniciales). URL: /storage/avatars/{id}.</param>
/// <param name="PendingAvatarFileId">Subida en curso, si la hay.</param>
/// <param name="AvatarUploadStatus">"processing" | "failed" (pendiente vencida) | null.</param>
/// <param name="AvatarModerationStatus">T-20: "under_review" | "blocked" para AvatarModerationFileId, o null.</param>
public sealed record MyCustomerContactResponse(
    string? PrimaryPhone,
    string PrimaryEmail,
    Guid? AvatarFileId = null,
    Guid? PendingAvatarFileId = null,
    string? AvatarUploadStatus = null,
    string? AvatarModerationStatus = null,
    Guid? AvatarModerationFileId = null
);

public sealed record UpdateMyCustomerPhoneCommand(Guid TenantId, Guid CustomerId, Guid UserId, string? PhoneNumber);
