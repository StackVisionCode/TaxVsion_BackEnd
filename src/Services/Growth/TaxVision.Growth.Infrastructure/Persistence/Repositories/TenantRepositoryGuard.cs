using BuildingBlocks.Tenancy;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories;

/// <summary>
/// Defensa en profundidad: el <paramref name="tenantId"/> explícito ya viene del comando/query
/// resuelto en el controller (validado desde el JWT). Este guard solo agrega una segunda barrera
/// cuando además hay un ITenantContext ambiental cargado — en ese caso debe coincidir.
/// </summary>
/// <remarks>
/// <b>Bug histórico (2026-07-24)</b>: la versión previa exigía SIEMPRE `tenantContext.HasTenant`.
/// Esto rompía todo Growth cuando el repo corría dentro de un handler de Wolverine
/// (bus.InvokeAsync) o un consumer — el <see cref="ITenantContext"/> ambiental está en un scope
/// de DI distinto al que pobló el <see cref="JwtTenantContextMiddleware"/>, y llega vacío. El
/// guard entonces rechazaba TODA consulta y todos los repos devolvían null/no-op silenciosos
/// aunque el <paramref name="tenantId"/> explícito viniera correcto del comando.
///
/// El fix es la política correcta original: si el ambiental EXISTE debe coincidir (protege
/// contra callers que armen un tenantId espurio dentro de una request HTTP autenticada); si NO
/// existe (Wolverine/BackgroundService), confiar en el parámetro explícito ya validado. Esto es
/// exactamente el mismo criterio que usa el patrón <c>IgnoreQueryFilters()</c> aplicado al resto
/// de los repos del monorepo.
/// </remarks>
internal static class TenantRepositoryGuard
{
    public static bool Matches(ITenantContext tenantContext, Guid tenantId) =>
        tenantId != Guid.Empty && (!tenantContext.HasTenant || tenantContext.TenantId == tenantId);

    public static void EnsureMatches(ITenantContext tenantContext, Guid tenantId)
    {
        if (!Matches(tenantContext, tenantId))
            throw new InvalidOperationException("The entity does not belong to the active tenant scope.");
    }
}
