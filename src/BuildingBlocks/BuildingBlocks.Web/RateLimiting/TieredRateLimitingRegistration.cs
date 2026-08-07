using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Web.RateLimiting;

/// <summary>
/// Registra el resolver/evaluador que consume <see cref="RateLimitAttribute"/> — cada
/// microservicio piloto lo llama una vez desde su Program.cs (Fase 3), igual que
/// <c>AddActorTypeAuthorization()</c>. Auto-registra <c>IRateLimitAlgorithmCounter</c> (Redis-backed,
/// algoritmo-consciente — cierra el hallazgo #8 de la auditoría post-Fase-9) usando el
/// <c>IConnectionMultiplexer</c> que el servicio ya tiene registrado para otros fines (session
/// denylist, caching, F26); no requiere ningún registro adicional del servicio consumidor a
/// diferencia de <c>IRateCounter</c> (patrón F26, primitivo de incremento simple sin algoritmo,
/// usado por consumidores no relacionados con políticas de catálogo — login throttler, limiters de
/// proveedor — que ese sí sigue siendo responsabilidad de cada servicio registrar por separado).
///
/// <para>
/// Nombre distinto de <c>RateLimitingRegistration</c> (namespace <c>BuildingBlocks.RateLimiting</c>,
/// mismo folder físico) a propósito — ese es el rate limiter previo del Gateway
/// (<c>AddTaxVisionGatewayRateLimiting</c>, `AddRateLimiter` de ASP.NET Core, Capa 1/infra), no
/// relacionado con este mecanismo por-tenant/por-usuario de Fase 3. No se tocan entre sí.
/// </para>
///
/// <para>
/// Los lectores de plan (<see cref="ITenantPlanCodeReader"/>/<see cref="IPlanRateLimitReader"/>)
/// se registran acá con sus implementaciones "null" por default — todo cae en fail-open a la
/// cuota base sin escalar (ver <c>NullTenantPlanCodeReader</c>) hasta que Fase 6 conecte un
/// lector real. Un servicio que ya tenga uno real debe registrarlo ANTES de llamar a este método
/// (<c>TryAddSingleton</c> respeta el primero que gane).
/// </para>
/// </summary>
public static class TieredRateLimitingRegistration
{
    public static IServiceCollection AddTieredRateLimiting(this IServiceCollection services)
    {
        services.AddSingleton<IRateLimitPolicyRegistry, RateLimitPolicyRegistry>();
        services.TryAddSingleton<ITenantPlanCodeReader, NullTenantPlanCodeReader>();
        services.TryAddSingleton<IPlanRateLimitReader, NullPlanRateLimitReader>();
        services.AddSingleton<IRateLimitQuotaResolver, RateLimitQuotaResolver>();
        services.AddSingleton<RateLimitMetrics>();
        services.TryAddSingleton<IRateLimitAlgorithmCounter, RedisRateLimitAlgorithmCounter>();
        services.AddSingleton<ITieredRateLimitEvaluator, TieredRateLimitEvaluator>();
        // Default no-op para la auditoría de 429 en políticas de categoría M. Un servicio con al
        // menos una política M registra su sink real con AddScoped&lt;IRateLimitAuditSink, TSink&gt;()
        // después de este método: el TryAdd solo evita pisar un registro previo, y un Add posterior
        // gana igualmente en la resolución simple. Así ambos órdenes funcionan.
        services.TryAddScoped<IRateLimitAuditSink, NoOpRateLimitAuditSink>();
        return services;
    }
}
