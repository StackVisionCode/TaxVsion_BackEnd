namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Identidad ya autenticada que viaja de la entrada central (<c>app.*</c>) al subdominio de la
/// oficina. No es la sesión: es un vale que la representa dentro de una ventana corta, para que el
/// canje materialice los tokens en el otro origen sin re-pedir credenciales.
/// <para>
/// <see cref="MustEnrollMfa"/>: el usuario necesita segundo factor por política pero aún no tiene
/// ninguno confirmado. Igual que el login directo, se le deja entrar con este flag para que el
/// frontend le fuerce el enrolamiento — no hay código que verificar todavía.
/// </para>
/// </summary>
public sealed record HandoffTicketPayload(Guid TenantId, Guid UserId, bool MustEnrollMfa = false);

/// <summary>
/// Vale de handoff cross-dominio: <b>de un solo uso</b> (GETDEL) y TTL corto. Se emite en el host
/// central tras autenticar (password + MFA si tocaba) y se canjea en el host de la oficina. Vive en
/// Redis, no en una tabla: es efímero y no es un aggregate.
/// </summary>
public interface IHandoffTicketStore
{
    Task<Guid> IssueAsync(HandoffTicketPayload payload, CancellationToken ct = default);

    /// <summary>Lee y borra atómicamente. Un segundo canje del mismo vale devuelve <c>null</c>.</summary>
    Task<HandoffTicketPayload?> ConsumeAsync(Guid ticket, CancellationToken ct = default);
}
