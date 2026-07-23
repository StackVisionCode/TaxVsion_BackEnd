using BuildingBlocks.Domain;

namespace TaxVision.Scribe.Domain.Projections;

/// <summary>
/// Dedup para ScribeTenantLogoMissingDetectedIntegrationEvent — el plan pide "a lo sumo 1 por
/// tenant por día"; esta fila guarda la última fecha en que se publicó para ese tenant. PK simple
/// TenantId, igual criterio que TenantLogoRef.
/// </summary>
public sealed class TenantLogoMissingNotification : ITenantOwned
{
    private TenantLogoMissingNotification() { }

    /// <summary>
    /// RBAC Fase 5 (RBAC_Hardening_Plan.md) — implementación de <see cref="ITenantOwned"/> para el
    /// <c>HasQueryFilter</c> global de <c>ScribeDbContext</c>. <see cref="TenantId"/> ya se fija una
    /// sola vez en <see cref="Create"/> — este método existe solo para satisfacer la interfaz, EF
    /// Core nunca lo invoca.
    /// </summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public Guid TenantId { get; private set; }
    public DateTime LastDetectedAtUtc { get; private set; }

    public static TenantLogoMissingNotification Create(Guid tenantId, DateTime detectedAtUtc) =>
        new() { TenantId = tenantId, LastDetectedAtUtc = detectedAtUtc };

    public void Touch(DateTime detectedAtUtc) => LastDetectedAtUtc = detectedAtUtc;

    public bool AlreadyNotifiedOn(DateTime dateUtc) => LastDetectedAtUtc.Date == dateUtc.Date;
}
