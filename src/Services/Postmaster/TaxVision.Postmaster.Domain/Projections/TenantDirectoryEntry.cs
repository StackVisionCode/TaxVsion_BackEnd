namespace TaxVision.Postmaster.Domain.Projections;

/// <summary>
/// Proyección local del nombre de una oficina — mismo patrón que <see cref="ConnectedMailbox"/> y
/// que las proyecciones de <c>Tenant</c> de PaymentApp/PaymentClient. La alimenta
/// <c>TenantCreatedIntegrationEvent</c>, que ya trae <c>Name</c> y <c>SubDomain</c>.
///
/// <para>Existe para una sola cosa: poner a la oficina en el From cuando el correo acaba saliendo
/// por el proveedor del sistema en su nombre. Sin esto, el cliente lee <c>From: TaxVision</c> en la
/// factura de su preparador, que es correcto pero desconcertante.</para>
///
/// <para>Se proyecta en vez de consultarse por red porque armar el From está en el camino de cada
/// envío, y una llamada M2M ahí convertiría a Tenant en dependencia dura del correo: si Tenant se
/// cae, no sale ni un email. El único momento en que Postmaster sí llama a Tenant es el backfill de
/// arranque (<c>TenantDirectoryBackfillService</c>), fuera del camino de envío.</para>
///
/// <para>El renombre llega por <c>TenantUpdatedIntegrationEvent</c> (<see cref="ApplyTenantUpdate"/>),
/// ordenado por el <c>UpdatedAtUtc</c> de Tenant. El nombre comercial llega por
/// <c>TenantProfileChangedIntegrationEvent</c> (<see cref="ApplyProfile"/>), ordenado por su versión.</para>
/// </summary>
public sealed class TenantDirectoryEntry
{
    private TenantDirectoryEntry() { }

    /// <summary>El id del tenant. Es la clave: una oficina, una fila.</summary>
    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = default!;
    public string SubDomain { get; private set; } = default!;
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Sello <c>UpdatedAtUtc</c> de Tenant del último renombre aplicado; null = nombre del alta.</summary>
    public DateTime? SourceUpdatedAtUtc { get; private set; }

    /// <summary>Nombre comercial (DBA) del perfil de la oficina; null = se usa <see cref="Name"/>.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>Versión del último perfil aplicado; 0 = nunca llegó. Independiente del renombre.</summary>
    public long ProfileVersion { get; private set; }

    /// <summary>Nombre de la oficina en el From de sus correos: lo que ve el cliente.</summary>
    public string SenderName => DisplayName ?? Name;

    public static TenantDirectoryEntry Create(Guid tenantId, string name, string subDomain, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        return new TenantDirectoryEntry
        {
            TenantId = tenantId,
            Name = name.Trim(),
            SubDomain = subDomain?.Trim().ToLowerInvariant() ?? string.Empty,
            UpdatedAtUtc = nowUtc,
        };
    }

    /// <summary>Alta reentregada, backfill o cambio de subdominio. El nombre no pisa un renombre ya
    /// aplicado por <see cref="ApplyTenantUpdate"/>.</summary>
    public void Rename(string name, string subDomain, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (SourceUpdatedAtUtc is null)
            Name = name.Trim();
        SubDomain = subDomain?.Trim().ToLowerInvariant() ?? SubDomain;
        UpdatedAtUtc = nowUtc;
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
    /// (idempotente). Un nombre vacío queda en null. Devuelve <c>true</c> si se aplicó.</summary>
    public bool ApplyProfile(long version, string? displayName, DateTime nowUtc)
    {
        if (version <= ProfileVersion)
            return false;

        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        ProfileVersion = version;
        UpdatedAtUtc = nowUtc;
        return true;
    }
}
