namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Resuelve el host primario (subdominio de plataforma) de un tenant — ej. "manfer.taxproffice.com" —
/// para armar los links per-tenant de los correos. El subdominio no viaja en los eventos de Tasks, así
/// que se pide por M2M a Auth (con cache corto). Nunca lanza: devuelve null si no se pudo resolver, y
/// el caller cae al base fijo de <c>PortalOptions</c>.
/// </summary>
public interface ITenantHostResolver
{
    Task<string?> ResolveHostAsync(Guid tenantId, CancellationToken ct = default);
}
