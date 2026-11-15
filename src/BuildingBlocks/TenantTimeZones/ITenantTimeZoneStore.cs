namespace BuildingBlocks.TenantTimeZones;

/// <summary>Lee y escribe la proyección sobre el DbContext del servicio que la adopta.</summary>
public interface ITenantTimeZoneStore
{
    /// <summary>Fila rastreada para actualizarla, o null.</summary>
    Task<TenantTimeZoneProjection?> FindAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Solo la zona, sin rastrear (lectura del resolver).</summary>
    Task<string?> GetTimeZoneIdAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Agrega una fila nueva. No guarda.</summary>
    Task AddAsync(TenantTimeZoneProjection projection, CancellationToken ct = default);
}
