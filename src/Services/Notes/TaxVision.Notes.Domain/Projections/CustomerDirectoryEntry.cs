using BuildingBlocks.Domain;

namespace TaxVision.Notes.Domain.Projections;

/// <summary>
/// Fase 4B — proyección local delgada de un customer del tenant, alimentada por los eventos de
/// Customer (Created/Updated/Deactivated/Activated/Archived/Reactivated) y por el consumer masivo
/// de <c>CustomersBulkImportedIntegrationEvent</c>. Usada por Fase 5 (Application) para validar
/// SOFT que un customer existe al crear una nota sobre él, y para mostrar/buscar por nombre — ver
/// ADR-09 (00_Overview_Decisiones_Y_Alcance.md).
///
/// <para>
/// <c>DisplayName</c> nullable a propósito: el consumer masivo de bulk import solo trae IDs (sin
/// PII), así que una fila creada desde ahí nace sin nombre — <see cref="CustomerDirectoryReconciliationJob"/>
/// lo rellena después vía M2M. Nunca "reventar" el flujo de creación de una nota por falta de
/// nombre — <c>DisplayName</c> es cosmético, la existencia del customer es lo único que importa.
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

    /// <summary>RBAC Fase 5 (RBAC_Hardening_Plan.md) — ver <c>Compose.Draft.SetTenant</c> en Correspondence.</summary>
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
    /// Idempotente: (1) nunca "va hacia atrás" si <paramref name="observedAtUtc"/> es más viejo que
    /// el estado actual (los eventos de Customer no traen número de revisión, así que se usa
    /// <c>evt.OccurredOn</c> como sustituto de orden temporal); (2) nunca pisa un
    /// <see cref="DisplayName"/> ya conocido con <c>null</c> — un bulk import o un evento de status
    /// sin nombre no debe borrar el nombre que ya se reconcilió.
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

    /// <summary>Reconciliación de nombre (job de background) — no toca Status ni retrocede en el tiempo.</summary>
    public void ApplyDisplayNameIfMissing(string displayName)
    {
        if (DisplayName is not null)
            return;
        DisplayName = displayName;
    }
}
