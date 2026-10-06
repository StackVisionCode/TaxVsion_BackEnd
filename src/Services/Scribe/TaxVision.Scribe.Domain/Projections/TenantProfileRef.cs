using BuildingBlocks.Domain;

namespace TaxVision.Scribe.Domain.Projections;

/// <summary>
/// Nombre de la oficina, para la cáscara de los correos que ella firma (<c>tenant-base</c>: cabecera
/// cuando no hay logo, y pie). Proyección local alimentada por <c>TenantCreatedIntegrationEvent</c>;
/// Tenant es la fuente de verdad. Hermana de <see cref="TenantLogoRef"/>, mismo criterio de PK.
///
/// <para>Se proyecta en vez de preguntarle a Tenant en cada render: una caída suya no puede dejar a la
/// plataforma sin mandar correos. Lo que sí queda pendiente es el renombre — hoy no existe un evento
/// de actualización de tenant, así que una oficina que cambie de nombre conserva el viejo acá.</para>
/// </summary>
public sealed class TenantProfileRef : ITenantOwned
{
    private TenantProfileRef() { }

    /// <summary>Solo para el <c>HasQueryFilter</c> de <c>ScribeDbContext</c>; EF nunca lo invoca.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string SubDomain { get; private set; } = default!;
    public DateTime UpdatedAtUtc { get; private set; }

    public static TenantProfileRef Create(Guid tenantId, string name, string subDomain, DateTime updatedAtUtc) =>
        new()
        {
            TenantId = tenantId,
            Name = name.Trim(),
            SubDomain = subDomain.Trim(),
            UpdatedAtUtc = updatedAtUtc,
        };

    public void Update(string name, string subDomain, DateTime updatedAtUtc)
    {
        Name = name.Trim();
        SubDomain = subDomain.Trim();
        UpdatedAtUtc = updatedAtUtc;
    }
}
