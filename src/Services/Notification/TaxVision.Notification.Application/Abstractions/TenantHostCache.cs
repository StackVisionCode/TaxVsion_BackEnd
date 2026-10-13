using System.Collections.Concurrent;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Host primario de cada oficina ya resuelto, para no pedirlo a Auth en cada correo. Se vacía cuando
/// el host deja de ser válido: la oficina renombró su subdominio o la plataforma cambió de dominio.
/// Singleton: lo comparten el resolver (que lo llena) y los consumers (que lo invalidan).
/// </summary>
public sealed class TenantHostCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<Guid, (string Host, DateTime ExpiresUtc)> _entries = new();

    public bool TryGet(Guid tenantId, out string host)
    {
        host = string.Empty;
        if (!_entries.TryGetValue(tenantId, out var entry) || entry.ExpiresUtc <= DateTime.UtcNow)
            return false;

        host = entry.Host;
        return true;
    }

    public void Set(Guid tenantId, string host) => _entries[tenantId] = (host, DateTime.UtcNow.Add(Ttl));

    public void Invalidate(Guid tenantId) => _entries.TryRemove(tenantId, out _);

    public void Clear() => _entries.Clear();
}
