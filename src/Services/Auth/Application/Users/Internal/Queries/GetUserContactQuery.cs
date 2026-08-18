using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Users.Internal.Queries;

public sealed record GetUserContactQuery(Guid TenantId, Guid UserId);

public sealed record UserContactResponse(string Email, string ActorType, bool IsActive);

/// <summary>
/// Recuperación pull del <b>correo</b> de un usuario — la contraparte de
/// <see cref="TaxVision.Auth.Application.Permissions.Internal.Queries.GetPermissionsSnapshotHandler"/>
/// para el directorio de Notification (Reminder Fase 10).
///
/// <para>
/// <b>Por qué existe.</b> Notification proyecta <c>userId → email</c> desde
/// <c>UserRegisteredIntegrationEvent</c>, que solo va hacia adelante: cualquier usuario registrado
/// antes de que esa proyección existiera no tiene fila, y sin este endpoint no recibiría correo
/// nunca. Mismo problema y misma solución que la Opción B de permisos.
/// </para>
///
/// <para>
/// <b>Devuelve el usuario inactivo en vez de fallar.</b> El caller necesita distinguir «no existe»
/// (no hay a quién escribirle) de «existe pero está inactivo» (hay dirección, pero no corresponde
/// escribirle); colapsar ambos en un error los volvería indistinguibles desde el otro lado de HTTP.
/// El filtro global de tenant no es confiable en el scope de una request M2M, así que el par
/// <c>(userId, tenantId)</c> se compara explícito — igual que el handler de permisos.
/// </para>
/// </summary>
public static class GetUserContactHandler
{
    public static async Task<Result<UserContactResponse>> Handle(
        GetUserContactQuery query,
        IUserRepository users,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(query.UserId, ct);
        if (user is null || user.TenantId != query.TenantId)
            return Result.Failure<UserContactResponse>(
                new Error("Auth.UserNotFound", "The user does not exist in the given tenant.")
            );

        return Result.Success(new UserContactResponse(user.Email, user.ActorType.ToString(), user.IsActive));
    }
}
