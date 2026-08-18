using System.Reflection;

namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Catálogo global de políticas de rate-limit — mismo patrón que
/// <c>TaxVision.Auth.Domain.Roles.PermissionCatalog</c>: constantes estáticas, sin referenciar
/// código de ningún servicio individual (invariante §3.10). Fuente de verdad para
/// <c>IRateLimitPolicyRegistry</c> y <see cref="TieredRateLimitEvaluator"/>.
///
/// <para>
/// Dividida en un archivo <c>partial</c> por servicio (<c>RateLimitPolicyCatalog.{Servicio}.cs</c>)
/// para mantener cada uno navegable; este archivo solo aloja el factory <see cref="Define"/>, el
/// registro (<see cref="All"/>/<see cref="GetByName"/>) y el cap de Capa 4. El historial detallado
/// de cada fase de migración vive en <c>documents/RateLimit/Plan_Implementacion_Fases.md</c> y en
/// la memoria del proyecto, no en comentarios de código.
/// </para>
/// </summary>
public static partial class RateLimitPolicyCatalog
{
    // Lazy + reflexión: los ~180 campos viven en archivos partial distintos (uno por servicio). Una
    // lista manual de nombres aquí dispararía CS8601 en cada entrada — el analizador de nulabilidad
    // no puede probar el orden de inicialización de field initializers ENTRE archivos de una misma
    // partial class (el orden real no está garantizado por el lenguaje) — y además habría que
    // recordar añadir cada campo nuevo a dos sitios. Reflexión sobre los propios campos evita ambos
    // problemas: se auto-registra, y no referencia los campos por nombre en tiempo de compilación.
    private static readonly Lazy<IReadOnlyDictionary<string, RateLimitPolicyDefinition>> ByNameLazy = new(() =>
        typeof(RateLimitPolicyCatalog)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(RateLimitPolicyDefinition))
            .Select(f => (RateLimitPolicyDefinition)f.GetValue(null)!)
            .ToDictionary(policy => policy.Name.Value)
    );

    private static IReadOnlyDictionary<string, RateLimitPolicyDefinition> ByName => ByNameLazy.Value;

    public static IReadOnlyCollection<RateLimitPolicyDefinition> All => ByName.Values.ToArray();

    public static RateLimitPolicyDefinition GetByName(string name) =>
        ByName.TryGetValue(name, out var definition)
            ? definition
            : throw new KeyNotFoundException($"No rate limit policy registered with name '{name}'.");

    /// <summary>Capa 4 (§4 del plan) — multiplicador sobre el overlay para derivar el cap agregado por endpoint en H/I (ADR_017 §2.2).</summary>
    private const int EndpointCapMultiplier = 20;

    private static RateLimitPolicyDefinition Define(
        string name,
        RateLimitCategory category,
        RateLimitPartitionDimension primaryPartition,
        IReadOnlyCollection<RateLimitPartitionDimension> overlayLayers,
        int quota,
        int windowSeconds,
        RateLimitAlgorithm algorithm,
        int? overlayQuota = null
    ) =>
        new()
        {
            Name = RateLimitPolicyName.From(name),
            Category = category,
            PrimaryPartition = primaryPartition,
            OverlayLayers = overlayLayers,
            BaseQuotaPerMinute = quota,
            OverlayQuotaPerMinute = overlayQuota,
            WindowSeconds = windowSeconds,
            Algorithm = algorithm,
            EndpointCapPerWindow =
                (category == RateLimitCategory.H || category == RateLimitCategory.I) && overlayQuota is not null
                    ? overlayQuota.Value * EndpointCapMultiplier
                    : null,
        };
}
