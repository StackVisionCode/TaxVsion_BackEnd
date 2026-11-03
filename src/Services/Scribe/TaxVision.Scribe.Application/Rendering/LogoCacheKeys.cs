using Microsoft.Extensions.Caching.Memory;
using TaxVision.Scribe.Domain;

namespace TaxVision.Scribe.Application.Rendering;

/// <summary>Claves del L1 de logos. La del tenant lleva la audiencia: Staff y Client resuelven distinto.</summary>
public static class LogoCacheKeys
{
    public const string System = "logo:system";

    public static string Tenant(Guid tenantId, RenderAudience audience) => $"logo:{tenantId}:{audience}";

    /// <summary>Cambió un logo del tenant: invalida todas las audiencias (el del CRM es respaldo del cliente).</summary>
    public static void ForgetTenant(IMemoryCache cache, Guid tenantId)
    {
        foreach (var audience in Enum.GetValues<RenderAudience>())
            cache.Remove(Tenant(tenantId, audience));
    }
}
