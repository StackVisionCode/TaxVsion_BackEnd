using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Pone en fila los intentos de login de una cuenta hasta el commit y refresca <c>user</c>. El contador de fallos
/// es el único camino que escribe en <c>Users</c> por tráfico anónimo: sin esto, el token de concurrencia
/// respondería 409 solo a las cuentas que existen.
/// </summary>
public interface IUserLoginLock
{
    Task AcquireAsync(User user, CancellationToken ct);
}
