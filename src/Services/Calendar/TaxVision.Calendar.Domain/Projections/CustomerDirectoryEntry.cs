using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Projections;

/// <summary>
/// Proyección local delgada de un customer del tenant, alimentada por los eventos de Customer y por
/// el consumer de import masivo. Task la usa para mostrar y buscar por cliente sin llamar a Customer,
/// y para la validación *soft* de <c>CustomerId</c> al crear una citas.
///
/// <para>
/// <see cref="DisplayName"/> es nullable a propósito: el evento de import masivo solo trae IDs, sin
/// PII, así que una fila creada desde ahí nace sin nombre y el job de reconciliación lo rellena
/// después. El nombre es cosmético; lo que importa es que el customer exista.
/// </para>
/// </summary>
public sealed class CustomerDirectoryEntry : ITenantOwned
{
    private CustomerDirectoryEntry() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string? DisplayName { get; private set; }
    public CustomerDirectoryStatus Status { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static CustomerDirectoryEntry Create(
        Guid tenantId,
        Guid customerId,
        string? displayName,
        CustomerDirectoryStatus status,
        DateTime observedAtUtc
    )
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (customerId == Guid.Empty)
            throw new ArgumentException("CustomerId is required.", nameof(customerId));

        var entry = new CustomerDirectoryEntry
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            DisplayName = displayName,
            Status = status,
            UpdatedAtUtc = observedAtUtc,
        };
        entry.SetTenant(tenantId);
        return entry;
    }

    /// <summary>
    /// Idempotente en dos sentidos: no retrocede si <paramref name="observedAtUtc"/> es más viejo que
    /// lo ya aplicado —los eventos de Customer no traen número de revisión, así que el orden lo da el
    /// tiempo de ocurrencia— y nunca pisa con <c>null</c> un nombre ya conocido, porque un evento de
    /// cambio de status o un import masivo llegan sin nombre y borrarían el que ya se reconcilió.
    /// </summary>
    public void ApplyIfNewer(string? displayName, CustomerDirectoryStatus status, DateTime observedAtUtc)
    {
        if (observedAtUtc < UpdatedAtUtc)
            return;

        if (displayName is not null)
            DisplayName = displayName;
        Status = status;
        UpdatedAtUtc = observedAtUtc;
    }

    /// <summary>Sólo para el job de reconciliación de nombres: no toca el status ni retrocede en el tiempo.</summary>
    public void ApplyDisplayNameIfMissing(string displayName)
    {
        if (DisplayName is not null)
            return;
        DisplayName = displayName;
    }
}
