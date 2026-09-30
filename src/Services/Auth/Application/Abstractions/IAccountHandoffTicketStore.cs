namespace TaxVision.Auth.Application.Abstractions;

/// <summary>Sesión del CRM que el Account del Landing va a compartir (mismo <c>sid</c>).</summary>
public sealed record AccountHandoffPayload(Guid TenantId, Guid UserId, Guid SessionId);

/// <summary>
/// Vale del botón "Manage subscription": <b>de un solo uso</b> (GETDEL) y TTL corto. Lo emite el CRM con su
/// sesión viva y lo canjea el Landing para sumar su propia cadena de refresh a esa misma sesión.
/// </summary>
public interface IAccountHandoffTicketStore
{
    TimeSpan Lifetime { get; }

    Task<Guid> IssueAsync(AccountHandoffPayload payload, CancellationToken ct = default);

    /// <summary>Lee y borra atómicamente. Un segundo canje del mismo vale devuelve <c>null</c>.</summary>
    Task<AccountHandoffPayload?> ConsumeAsync(Guid ticket, CancellationToken ct = default);
}
