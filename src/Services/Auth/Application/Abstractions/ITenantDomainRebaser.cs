namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Re-apunta los hosts guardados en <c>TenantDomains</c> al dominio vigente de la plataforma: cada
/// subdominio a <c>{slug}.{baseDomain}</c> y la fila del tenant Platform a <c>apiHost</c>. La
/// resolución ya no depende de esos hosts (va por slug), pero son los que viajan en eventos y se
/// muestran, así que se mantienen al día. Idempotente; devuelve cuántas filas movió.
/// </summary>
public interface ITenantDomainRebaser
{
    Task<int> RebaseAsync(string baseDomain, string apiHost, CancellationToken ct = default);
}
