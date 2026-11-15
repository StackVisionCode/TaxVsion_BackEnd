using BuildingBlocks.TimeZones;

namespace BuildingBlocks.TenantTimeZones;

/// <summary>Copia local de la zona IANA de una oficina (dueño: Tenant). Una fila por tenant.</summary>
public sealed class TenantTimeZoneProjection
{
    private TenantTimeZoneProjection() { }

    public Guid TenantId { get; private set; }
    public string TimeZoneId { get; private set; } = IanaTimeZone.UtcId;

    /// <summary>Versión de la oficina en Tenant; null en eventos viejos o en el alta.</summary>
    public long? SourceVersion { get; private set; }

    /// <summary>Momento del cambio en Tenant; ordena cuando falta la versión.</summary>
    public DateTime SourceUpdatedAtUtc { get; private set; }

    public static TenantTimeZoneProjection Create(Guid tenantId, string timeZoneId, long? sourceVersion, DateTime sourceUpdatedAtUtc) =>
        new()
        {
            TenantId = tenantId,
            TimeZoneId = timeZoneId,
            SourceVersion = sourceVersion,
            SourceUpdatedAtUtc = sourceUpdatedAtUtc,
        };

    /// <summary>Con las dos versiones manda la versión; si falta alguna, manda la hora del cambio.</summary>
    public bool IsOlderThan(long? sourceVersion, DateTime sourceUpdatedAtUtc) =>
        sourceVersion is { } incoming && SourceVersion is { } applied
            ? incoming > applied
            : sourceUpdatedAtUtc > SourceUpdatedAtUtc;

    public void Apply(string timeZoneId, long? sourceVersion, DateTime sourceUpdatedAtUtc)
    {
        TimeZoneId = timeZoneId;
        SourceVersion = sourceVersion ?? SourceVersion;
        SourceUpdatedAtUtc = sourceUpdatedAtUtc;
    }

    /// <summary>
    /// Corrige con el estado actual leído de Tenant (sin versión). Marca la hora de lectura: un evento más viejo
    /// que llegue después no lo deshace; uno más nuevo, sí.
    /// </summary>
    public void Reconcile(string timeZoneId, DateTime readAtUtc)
    {
        TimeZoneId = timeZoneId;
        if (readAtUtc > SourceUpdatedAtUtc)
            SourceUpdatedAtUtc = readAtUtc;
    }
}
