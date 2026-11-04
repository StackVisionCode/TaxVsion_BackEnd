using BuildingBlocks.Domain;

namespace TaxVision.Scribe.Domain.Projections;

/// <summary>
/// Nombre de la oficina, para la cáscara de los correos que ella firma (<c>tenant-base</c>: cabecera
/// cuando no hay logo, y pie). Proyección local alimentada por <c>TenantCreatedIntegrationEvent</c> y
/// <c>TenantUpdatedIntegrationEvent</c> (renombre), más nombre comercial y dirección postal desde
/// <c>TenantProfileChangedIntegrationEvent</c>; Tenant es la fuente de verdad. Hermana de
/// <see cref="TenantLogoRef"/>, mismo criterio de PK.
///
/// <para>Se proyecta en vez de preguntarle a Tenant en cada render: una caída suya no puede dejar a la
/// plataforma sin mandar correos.</para>
/// </summary>
public sealed class TenantProfileRef : ITenantOwned
{
    private TenantProfileRef() { }

    /// <summary>Solo para el <c>HasQueryFilter</c> de <c>ScribeDbContext</c>; EF nunca lo invoca.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string SubDomain { get; private set; } = default!;

    /// <summary>Último toque local (alta, backfill, subdominio o renombre), con el reloj de Scribe.</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Sello <c>UpdatedAtUtc</c> de Tenant del último renombre aplicado. Ordena los
    /// <c>TenantUpdatedIntegrationEvent</c>; null = el nombre es el del alta.</summary>
    public DateTime? SourceUpdatedAtUtc { get; private set; }

    /// <summary>Nombre comercial (DBA) del perfil de la oficina; null = se firma con <see cref="Name"/>.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>Dirección postal en una línea para el pie del correo; null = el pie va sin dirección.</summary>
    public string? PostalAddress { get; private set; }

    /// <summary>Versión del último perfil aplicado; 0 = nunca llegó. Independiente del renombre.</summary>
    public long ProfileVersion { get; private set; }

    /// <summary>Nombre con el que firma los correos que ve el cliente.</summary>
    public string SignatureName => DisplayName ?? Name;

    public static TenantProfileRef Create(Guid tenantId, string name, string subDomain, DateTime updatedAtUtc) =>
        new()
        {
            TenantId = tenantId,
            Name = name.Trim(),
            SubDomain = subDomain.Trim(),
            UpdatedAtUtc = updatedAtUtc,
        };

    /// <summary>Alta reentregada, backfill o cambio de subdominio. El nombre no pisa un renombre ya
    /// aplicado: ese llega solo por <see cref="ApplyTenantUpdate"/>.</summary>
    public void Update(string name, string subDomain, DateTime updatedAtUtc)
    {
        if (SourceUpdatedAtUtc is null)
            Name = name.Trim();
        SubDomain = subDomain.Trim();
        UpdatedAtUtc = updatedAtUtc;
    }

    /// <summary>Renombre publicado por Tenant. Ignora un evento más viejo que el aplicado (uno igual se
    /// reaplica: idempotente). Devuelve <c>true</c> si se aplicó.</summary>
    public bool ApplyTenantUpdate(string name, DateTime sourceUpdatedAtUtc, DateTime nowUtc)
    {
        if (SourceUpdatedAtUtc is { } applied && sourceUpdatedAtUtc < applied)
            return false;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        Name = name.Trim();
        SourceUpdatedAtUtc = sourceUpdatedAtUtc;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>Perfil publicado por Tenant. Ignora una versión menor o igual a la aplicada
    /// (idempotente). Un texto vacío queda en null. Devuelve <c>true</c> si se aplicó.</summary>
    public bool ApplyProfile(long version, string? displayName, string? postalAddress, DateTime nowUtc)
    {
        if (version <= ProfileVersion)
            return false;

        DisplayName = Normalize(displayName);
        PostalAddress = Normalize(postalAddress);
        ProfileVersion = version;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
