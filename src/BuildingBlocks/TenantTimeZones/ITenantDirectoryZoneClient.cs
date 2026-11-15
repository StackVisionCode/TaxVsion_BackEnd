namespace BuildingBlocks.TenantTimeZones;

/// <summary>Una oficina del directorio interno de Tenant, con su zona actual.</summary>
public sealed record TenantDirectoryZone(Guid TenantId, string TimeZoneId);

/// <summary>Lee <c>GET internal/tenants/directory</c> (Tenant, M2M). Devuelve null si Tenant no responde: nunca lanza.</summary>
public interface ITenantDirectoryZoneClient
{
    Task<IReadOnlyList<TenantDirectoryZone>?> ListPageAsync(int page, int size, CancellationToken ct = default);
}
