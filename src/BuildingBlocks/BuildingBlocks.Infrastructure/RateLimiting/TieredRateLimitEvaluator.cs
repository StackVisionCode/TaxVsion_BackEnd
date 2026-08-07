using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure.RateLimiting;

/// <summary>
/// Implementación de referencia de <see cref="ITieredRateLimitEvaluator"/> — evalúa, en orden
/// (§1 del plan — "la primera capa que dispare"), Capa 4 (cap global por endpoint, categorías
/// H/I), Capa 3 (primaria/"user") y Capa 2 (overlay/"tenant"), contra la cuota resuelta por
/// <see cref="IRateLimitQuotaResolver"/> y el algoritmo declarado por
/// <see cref="RateLimitPolicyDefinition.Algorithm"/> (vía <see cref="IRateLimitAlgorithmCounter"/>
/// — cierra el hallazgo #8 de la auditoría post-Fase-9: antes de esto todo corría como ventana fija
/// sin importar lo que la política declarara). Fail-open ante cualquier excepción del contador o
/// del resolver de cuota (invariante §3.3) — un Redis caído, o un fallo al resolver la cuota por
/// plan (caché/token M2M/HTTP a Subscription/deserialización), nunca debe bloquear tráfico ni
/// traducirse en un 500 (auditoría hallazgo #4 — antes de esto <c>quotaResolver.ResolveAsync</c>
/// no estaba protegido).
///
/// <para>
/// Fase 8 — emite <see cref="RateLimitMetrics"/> acá mismo (no en <c>RateLimitAttribute</c>): este
/// es el único lugar con contexto completo (policy, tenant, capa, y las 3 fuentes de fallback-open
/// — Redis primario, Redis overlay, resolución de plan vía <see cref="EffectiveQuota.IsFallback"/>).
/// </para>
/// </summary>
public sealed class TieredRateLimitEvaluator(
    IRateLimitAlgorithmCounter algorithmCounter,
    IRateLimitQuotaResolver quotaResolver,
    RateLimitMetrics metrics,
    ILogger<TieredRateLimitEvaluator> logger
) : ITieredRateLimitEvaluator
{
    public async Task<RateLimitVerdict> EvaluateAsync(
        RateLimitPolicyDefinition policy,
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var window = TimeSpan.FromSeconds(policy.WindowSeconds);
        var service = ServiceNameOf(policy.Name.Value);

        // Capa 4 (§4, categorías H/I) — cap agregado a través de TODOS los tenants, evaluado
        // antes que nada porque protege el recurso de infraestructura compartido, no la fairness
        // de un tenant individual (eso lo cubren las capas 2/3 más abajo). No depende de la cuota
        // resuelta por tenant/plan — es un número fijo del catálogo.
        if (policy.EndpointCapPerWindow is { } endpointCap)
        {
            var endpointKey = RateCounterKey.From(BuildKey(service, policy.Name.Value, ["endpoint"]));
            try
            {
                var endpointExceeded = await algorithmCounter
                    .EvaluateAsync(endpointKey, policy.Algorithm, endpointCap, window, ct)
                    .ConfigureAwait(false);
                if (endpointExceeded)
                {
                    metrics.RecordBlocked(policy.Name.Value, "endpoint", tenantId, "n/a");
                    return RateLimitVerdict.Exceeded("endpoint", endpointCap, policy.WindowSeconds);
                }
            }
            catch (Exception)
            {
                // Fail-open — invariante §3.3.
                metrics.RecordFallbackOpen(policy.Name.Value, "redis_endpoint");
            }
        }

        EffectiveQuota quota;
        try
        {
            quota = await quotaResolver.ResolveAsync(policy, tenantId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Fail-open — invariante §3.3. A diferencia de un contador Redis caído (capturado más
            // abajo por política/capa), un resolver que lanza (caché de plan caída, token M2M
            // fallido, HTTP a Subscription caído, catálogo indeserializable) nunca debe traducirse
            // en un 500 — cae al cupo base sin escalar, igual que el camino "no se pudo resolver"
            // que RateLimitQuotaResolver ya modela vía IsFallback.
            logger.LogWarning(
                ex,
                "RateLimit quota resolution failed for policy {Policy}, tenant {TenantId} — falling back to base quota.",
                policy.Name.Value,
                tenantId
            );
            quota = new EffectiveQuota(
                policy.BaseQuotaPerMinute,
                policy.WindowSeconds,
                IsFallback: true,
                OverlayPermitCount: policy.OverlayQuotaPerMinute
            );
        }

        var plan = quota.PlanCode ?? "n/a";

        if (quota.IsFallback)
            metrics.RecordFallbackOpen(policy.Name.Value, "quota_unresolved");

        // Construcción de la clave primaria fuera del try: un NotSupportedException acá es un bug
        // de wiring (categoría no soportada), no una falla de infra — debe propagar, no fail-open
        // en silencio.
        var primaryKey = RateCounterKey.From(
            BuildKey(service, policy.Name.Value, PrimaryParts(policy, tenantId, userId))
        );

        metrics.RecordEvaluated(policy.Name.Value, "user", tenantId, plan);
        try
        {
            var primaryExceeded = await algorithmCounter
                .EvaluateAsync(primaryKey, policy.Algorithm, quota.PermitCount, window, ct)
                .ConfigureAwait(false);
            if (primaryExceeded)
            {
                metrics.RecordBlocked(policy.Name.Value, "user", tenantId, plan);
                return RateLimitVerdict.Exceeded("user", quota.PermitCount, policy.WindowSeconds);
            }
        }
        catch (Exception)
        {
            // Fail-open — invariante §3.3.
            metrics.RecordFallbackOpen(policy.Name.Value, "redis_primary");
            return RateLimitVerdict.Allowed();
        }

        if (quota.OverlayPermitCount is { } overlayPermitCount)
        {
            // El evaluador genérico solo sabe construir un overlay particionado por Tenant — validar
            // en vez de asumir en silencio (hallazgo #12 de la auditoría post-Fase-9: antes de esto
            // OverlayLayers se declaraba pero nunca se leía, "funcionaba por coincidencia" porque
            // todas las políticas existentes con overlay numérico ya usaban [Tenant]).
            if (!policy.OverlayLayers.SequenceEqual([RateLimitPartitionDimension.Tenant]))
                throw new NotSupportedException(
                    $"TieredRateLimitEvaluator solo soporta OverlayLayers=[Tenant] — la política '{policy.Name}' "
                        + $"declara '{string.Join('|', policy.OverlayLayers)}'."
                );

            var overlayKey = RateCounterKey.From(
                BuildKey(service, policy.Name.Value, ["tenant", tenantId.ToString("N")])
            );
            metrics.RecordEvaluated(policy.Name.Value, "tenant", tenantId, plan);
            try
            {
                var overlayExceeded = await algorithmCounter
                    .EvaluateAsync(overlayKey, policy.Algorithm, overlayPermitCount, window, ct)
                    .ConfigureAwait(false);
                if (overlayExceeded)
                {
                    metrics.RecordBlocked(policy.Name.Value, "tenant", tenantId, plan);
                    return RateLimitVerdict.Exceeded("tenant", overlayPermitCount, policy.WindowSeconds);
                }
            }
            catch (Exception)
            {
                // fail-open solo para el overlay — la capa primaria ya se evaluó y pasó.
                metrics.RecordFallbackOpen(policy.Name.Value, "redis_overlay");
            }
        }

        return RateLimitVerdict.Allowed();
    }

    private static string ServiceNameOf(string policyName) => policyName[..policyName.IndexOf('.')];

    /// <summary>
    /// Dimensiones soportadas por este evaluador genérico: Tenant+User combinado (Bloque II), User
    /// solo (categoría N) y Tenant solo (categoría J). Comparación EXACTA (no <c>HasFlag</c>) a
    /// propósito — hallazgo #12 de la auditoría post-Fase-9: con <c>HasFlag</c>, una partición
    /// compuesta como K (Tenant+AccountOrProvider) pasaba silenciosamente por la rama "solo Tenant"
    /// e ignoraba AccountOrProvider en vez de lanzar. K ya no pasa por acá (su overlay lo maneja
    /// <c>IProviderRateLimiter</c> desde F26) pero si algún día alguien decora un endpoint K con
    /// <c>[RateLimit]</c> por error, esto debe lanzar, no construir una clave incompleta.
    /// </summary>
    private static string[] PrimaryParts(RateLimitPolicyDefinition policy, Guid tenantId, Guid userId)
    {
        const RateLimitPartitionDimension TenantAndUser =
            RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User;

        if (policy.PrimaryPartition == TenantAndUser)
            return ["tenant", tenantId.ToString("N"), "user", userId.ToString("N")];
        if (policy.PrimaryPartition == RateLimitPartitionDimension.User)
            return ["user", userId.ToString("N")];
        if (policy.PrimaryPartition == RateLimitPartitionDimension.Tenant)
            return ["tenant", tenantId.ToString("N")];

        throw new NotSupportedException(
            $"TieredRateLimitEvaluator no soporta la partición primaria '{policy.PrimaryPartition}' de la política '{policy.Name}' — "
                + "solo Tenant|User, User y Tenant están implementados."
        );
    }

    private static string BuildKey(string service, string policyName, IReadOnlyCollection<string> parts) =>
        $"{service}:rl:{policyName}:{string.Join(':', parts)}";
}
