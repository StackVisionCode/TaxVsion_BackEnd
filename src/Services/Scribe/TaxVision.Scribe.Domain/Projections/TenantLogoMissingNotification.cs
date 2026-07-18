namespace TaxVision.Scribe.Domain.Projections;

/// <summary>
/// Dedup para ScribeTenantLogoMissingDetectedIntegrationEvent — el plan pide "a lo sumo 1 por
/// tenant por día"; esta fila guarda la última fecha en que se publicó para ese tenant. PK simple
/// TenantId, igual criterio que TenantLogoRef.
/// </summary>
public sealed class TenantLogoMissingNotification
{
    private TenantLogoMissingNotification() { }

    public Guid TenantId { get; private set; }
    public DateTime LastDetectedAtUtc { get; private set; }

    public static TenantLogoMissingNotification Create(Guid tenantId, DateTime detectedAtUtc) =>
        new() { TenantId = tenantId, LastDetectedAtUtc = detectedAtUtc };

    public void Touch(DateTime detectedAtUtc) => LastDetectedAtUtc = detectedAtUtc;

    public bool AlreadyNotifiedOn(DateTime dateUtc) => LastDetectedAtUtc.Date == dateUtc.Date;
}
