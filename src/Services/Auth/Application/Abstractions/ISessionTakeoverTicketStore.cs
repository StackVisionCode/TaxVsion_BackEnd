namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Contexto que viaja en el vale de takeover, entre "login ya autenticado con sesión previa activa"
/// y su confirmación en <c>POST /auth/session/takeover</c>. Guarda lo mínimo para re-emitir la sesión
/// tras revocar las anteriores; el usuario y el tenant se revalidan frescos al canjear.
/// <para>
/// <see cref="MustEnrollMfa"/>: el login venía por la rama de "MFA requerido sin método", así que al
/// confirmar el takeover la respuesta debe conservar el flag para que el frontend fuerce el setup.
/// </para>
/// </summary>
public sealed record SessionTakeoverPayload(
    Guid TenantId,
    Guid UserId,
    string[] AuthMethods,
    string? DeviceName,
    bool MustEnrollMfa = false
);

/// <summary>
/// Vale de sesión única: <b>de un solo uso</b> (GETDEL) y TTL corto. Se emite cuando un login ya
/// autenticado (password + MFA si tocaba) detecta una sesión previa activa, en vez de emitir tokens.
/// El frontend muestra el interstitial y, si el usuario confirma, lo canjea para revocar las sesiones
/// anteriores y crear la nueva. Vive en Redis: es efímero y no es un aggregate. Espejo de
/// <see cref="IHandoffTicketStore"/>.
/// </summary>
public interface ISessionTakeoverTicketStore
{
    Task<Guid> IssueAsync(SessionTakeoverPayload payload, CancellationToken ct = default);

    /// <summary>Lee y borra atómicamente. Un segundo canje del mismo vale devuelve <c>null</c>.</summary>
    Task<SessionTakeoverPayload?> ConsumeAsync(Guid ticket, CancellationToken ct = default);
}
