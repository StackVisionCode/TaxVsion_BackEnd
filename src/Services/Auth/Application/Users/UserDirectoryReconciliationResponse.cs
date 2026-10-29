namespace TaxVision.Auth.Application.Users;

/// <summary>
/// Fila del directorio de usuarios con la que Communication repara su <c>UserDirectoryEntry</c> cuando se
/// pierde un evento. Todos los actor types: staff, cuentas de portal y PlatformAdmin (soporte).
/// </summary>
public sealed record UserDirectoryReconciliationResponse(
    Guid TenantId,
    Guid UserId,
    string ActorType,
    bool IsActive,
    string DisplayName,
    string Email,
    Guid? AvatarFileId
);
