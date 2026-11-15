namespace BuildingBlocks.TenantTimeZones;

/// <summary>Token M2M de servicio para leer el directorio de Tenant; cada servicio lo da con su propio mecanismo M2M.</summary>
public interface ITenantTimeZoneTokenProvider
{
    Task<string?> GetServiceTokenAsync(CancellationToken ct = default);
}
