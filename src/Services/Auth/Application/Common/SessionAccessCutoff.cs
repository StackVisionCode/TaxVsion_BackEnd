using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Cortar el acceso de verdad, en los tres pasos que hacen falta y en el orden correcto (A5, cierra G10
/// y R12 del plan):
///
/// <list type="number">
/// <item><b>Denylist</b> del <c>sid</c> en Redis: es lo único que invalida un access token ya emitido.
/// Revocar en la base solo corta el <i>próximo</i> refresh, así que sin este paso el token seguía
/// sirviendo hasta 15 minutos — el agujero exacto que tenían la suspensión del tenant y el bloqueo por
/// facturación.</item>
/// <item><b>Anuncio</b> por Pub/Sub para que Communication empuje el logout a los otros dispositivos
/// del usuario (evento de socket <c>session.revoked</c>). Sin esto, la pestaña abierta se quedaba con
/// la sesión muerta hasta el siguiente request.</item>
/// <item><b>Revocación</b> en la base, que es la autoritativa y persiste el motivo.</item>
/// </list>
///
/// <para>
/// El orden importa: primero se cierra la puerta (denylist), después se avisa. Al revés, un cliente
/// avisado podría alcanzar a usar el token viejo.
/// </para>
/// <para>
/// El anuncio es best-effort por contrato (<see cref="ISessionRevocationPublisher"/> no propaga
/// excepciones): un Redis caído no debe impedir revocar.
/// </para>
/// </summary>
public static class SessionAccessCutoff
{
    /// <summary>
    /// TTL de la entrada en la denylist. Tiene que cubrir la vida restante máxima de un access token
    /// (15 min) con margen; el mismo valor que ya usaban la baja y el offboard.
    /// </summary>
    public static readonly TimeSpan DenylistTtl = TimeSpan.FromMinutes(20);

    /// <summary>Corta el acceso de un usuario. Devuelve cuántas sesiones se revocaron.</summary>
    public static async Task<int> ForUserAsync(
        User target,
        string reason,
        ISessionRepository sessions,
        IAccessTokenDenylist denylist,
        ISessionRevocationPublisher revocations,
        CancellationToken ct
    )
    {
        var active = await sessions.GetActiveSessionsByUserAsync(target.Id, ct);
        await CutAsync(active, reason, denylist, revocations, ct);
        return await sessions.RevokeAllForUserAsync(target.Id, reason, null, ct);
    }

    /// <summary>Corta el acceso de un tenant completo (staff y clientes por igual: un tenant suspendido
    /// no atiende a nadie). Devuelve cuántas sesiones se revocaron.</summary>
    public static async Task<int> ForTenantAsync(
        Guid tenantId,
        string reason,
        ISessionRepository sessions,
        IAccessTokenDenylist denylist,
        ISessionRevocationPublisher revocations,
        CancellationToken ct
    )
    {
        var active = await sessions.GetActiveSessionsByTenantAsync(tenantId, ct);
        await CutAsync(active, reason, denylist, revocations, ct);
        return await sessions.RevokeAllForTenantAsync(tenantId, reason, ct);
    }

    private static async Task CutAsync(
        IReadOnlyList<UserSession> active,
        string reason,
        IAccessTokenDenylist denylist,
        ISessionRevocationPublisher revocations,
        CancellationToken ct
    )
    {
        foreach (var session in active)
            await denylist.DenySessionAsync(session.Id, DenylistTtl, ct);

        foreach (var session in active)
            await revocations.PublishRevokedAsync(session.TenantId, session.UserId, session.Id, reason, ct);
    }
}
