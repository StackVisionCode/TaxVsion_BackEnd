namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Extrae los módulos habilitados (<c>module.*</c>) del snapshot de entitlements de un
/// <see cref="TenantEntitlementsChangedIntegrationEvent"/>. Lógica compartida — antes vivía copiada
/// en <c>Auth.TenantEntitlementsChangedConsumer.ExtractEnabledModules</c>; centralizarla evita que
/// cada consumidor la reimplemente cuando el gate de módulo se sume a más servicios.
/// </summary>
public static class TenantEntitlementModuleExtensions
{
    private const string ModuleFeaturePrefix = "module.";

    /// <summary>
    /// Nombres de módulo habilitados (sin el prefijo <c>module.</c>), ej. <c>["signatures", "documents"]</c>.
    /// Una entrada cuenta como habilitada solo si su valor parsea a <c>true</c>.
    /// </summary>
    public static string[] ExtractEnabledModules(this TenantEntitlementsChangedIntegrationEvent evt) =>
        ExtractEnabledModules(evt.EntitlementValues);

    public static string[] ExtractEnabledModules(IReadOnlyDictionary<string, string> entitlementValues)
    {
        var modules = new List<string>();
        foreach (var (key, value) in entitlementValues)
        {
            if (
                key.StartsWith(ModuleFeaturePrefix, StringComparison.Ordinal)
                && bool.TryParse(value, out var enabled)
                && enabled
            )
                modules.Add(key[ModuleFeaturePrefix.Length..]);
        }

        return modules.ToArray();
    }
}
