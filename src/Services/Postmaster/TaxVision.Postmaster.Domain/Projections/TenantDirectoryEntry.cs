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
/// <para>No hay forma de renombrar un tenant en el producto (el aggregate solo expone
/// <c>Suspend</c>/<c>ChangeStatus</c>), así que no se consume ningún evento de actualización: sería
/// cablear un camino para algo que no puede pasar. El día que exista el renombrado, este es el sitio.</para>
/// </summary>
public sealed class TenantDirectoryEntry
{
    private TenantDirectoryEntry() { }

    /// <summary>El id del tenant. Es la clave: una oficina, una fila.</summary>
    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = default!;
    public string SubDomain { get; private set; } = default!;
    public DateTime UpdatedAtUtc { get; private set; }

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

    public void Rename(string name, string subDomain, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        Name = name.Trim();
        SubDomain = subDomain?.Trim().ToLowerInvariant() ?? SubDomain;
        UpdatedAtUtc = nowUtc;
    }
}
