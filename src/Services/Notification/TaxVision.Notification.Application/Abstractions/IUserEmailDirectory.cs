using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Acceso a la proyección local <c>userId → email</c>.</summary>
public interface IUserEmailDirectoryRepository
{
    Task<UserEmailDirectoryEntry?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task AddAsync(UserEmailDirectoryEntry entry, CancellationToken ct = default);
}

/// <summary>Correo de un usuario tal como lo conoce Auth, para poblar o reparar el directorio.</summary>
public sealed record RemoteUserContact(string Email, bool IsActive);

/// <summary>
/// Recuperación pull contra el endpoint interno de Auth. <b>Nunca lanza</b>: devuelve <c>null</c>
/// ante cualquier fallo de token, HTTP o 404, y el caller decide — un usuario que no existe y un
/// Auth caído tienen que verse igual desde acá, porque en ambos casos la única respuesta correcta
/// es «no mando el correo».
/// </summary>
public interface IUserContactSnapshotClient
{
    Task<RemoteUserContact?> FetchContactAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
