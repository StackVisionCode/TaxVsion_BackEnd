namespace TaxVision.Customer.Application.Abstractions;

public sealed record RemoteStaffUser(Guid TenantId, Guid UserId, string ActorType, bool IsActive, bool IsOffboarded);

public sealed record RemoteStaffPage(IReadOnlyList<RemoteStaffUser> Items, bool HasMore);

/// <summary>
/// Lista el staff de todos los tenants tal como lo ve Auth, para reconciliar el directorio de empleados.
/// Nunca lanza: null ante cualquier falla de token o HTTP, y la pasada se reintenta en el siguiente intervalo.
/// </summary>
public interface IAuthStaffDirectoryClient
{
    Task<RemoteStaffPage?> ListStaffAsync(int page, int size, CancellationToken ct = default);
}
