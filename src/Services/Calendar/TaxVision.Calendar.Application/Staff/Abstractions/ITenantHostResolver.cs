namespace TaxVision.Calendar.Application.Staff.Abstractions;

/// <summary>
/// El host de la oficina (<c>manfer.taxoffix.io</c>) desde Auth (M2M). Las páginas públicas lo usan como
/// clave de marca para pintar el logo de la oficina aunque se abran sin su subdominio. Null si falla.
/// </summary>
public interface ITenantHostResolver
{
    Task<string?> ResolveHostAsync(Guid tenantId, CancellationToken ct = default);
}
