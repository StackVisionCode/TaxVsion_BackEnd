using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web.RateLimiting;

/// <summary>
/// Aplica la política de rate-limit nombrada a una acción — Fase 3 del plan
/// (Plan_Implementacion_Fases.md §8). Implementado como <see cref="IAsyncResourceFilter"/> directo
/// sobre el atributo (mismo criterio ASP.NET Core que cualquier <c>[Authorize]</c>-like filter),
/// no como middleware global — necesita el nombre de política resuelto por endpoint, que solo
/// está disponible una vez que MVC ya hizo routing.
///
/// <para>
/// Auditoria RateLimit hallazgo #5 — antes implementaba <c>IAsyncActionFilter</c>, que en el
/// pipeline de MVC corre DESPUÉS del model binding (Autorización → Resource → Model Binding →
/// Action → Result). Para categorías caras (I/J: bulk/export) eso significa que el body entero ya
/// se deserializó antes de que el gate de rate-limit pudiera rechazar la request. Los resource
/// filters (<c>IAsyncResourceFilter</c>) corren ANTES del model binding — mismo punto del
/// pipeline que <c>[Authorize]</c> — así que ahora el 429 se devuelve sin gastar el costo de
/// parsear el body de una request que de todas formas se va a rechazar.
/// </para>
///
/// <para>
/// Resuelve tenant/user directo del <see cref="System.Security.Claims.ClaimsPrincipal"/> (no vía
/// <c>ControllerIdentityExtensions</c> — esos son extensions de <c>ControllerBase</c>, no
/// disponibles en un filtro). Si no puede resolver ambos, deja pasar el request sin contar
/// (fail-open — no hay antes de <c>[Authorize]</c> en el pipeline que garantice su presencia acá
/// para cada actor, y bloquear por un claim faltante sería peor que no gatear).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RateLimitAttribute(string policyName) : Attribute, IAsyncResourceFilter
{
    /// <summary>Nombre del parametro de ruta donde vive la credencial de una politica por token.</summary>
    public const string TokenRouteValue = "token";

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var registry = services.GetRequiredService<IRateLimitPolicyRegistry>();
        var evaluator = services.GetRequiredService<ITieredRateLimitEvaluator>();

        var policy = registry.GetByName(policyName);
        var user = context.HttpContext.User;

        // Una politica particionada por token no espera claims: la credencial esta en la ruta, y el
        // filtro corre despues del routing, asi que ya esta disponible. Sin esta rama el fail-open por
        // claims faltantes deja el endpoint sin limite ninguno, que es justo lo contrario de lo que
        // pide una URL publica.
        if (policy.PrimaryPartition == RateLimitPartitionDimension.Token)
        {
            var token = context.RouteData.Values[TokenRouteValue] as string;
            if (string.IsNullOrEmpty(token))
            {
                RecordMissingClaims(services, policyName);
                await next().ConfigureAwait(false);
                return;
            }

            var tokenVerdict = await evaluator
                .EvaluateAsync(policy, Guid.Empty, Guid.Empty, context.HttpContext.RequestAborted, token)
                .ConfigureAwait(false);

            if (!tokenVerdict.IsExceeded)
            {
                await next().ConfigureAwait(false);
                return;
            }

            await WriteRateLimitResponseAsync(context, policy, tokenVerdict).ConfigureAwait(false);
            return;
        }

        if (!user.TryGetTenantId(out var tenantId) || !user.TryGetUserId(out var userId))
        {
            RecordMissingClaims(services, policyName);
            await next().ConfigureAwait(false);
            return;
        }

        var verdict = await evaluator
            .EvaluateAsync(policy, tenantId, userId, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (!verdict.IsExceeded)
        {
            await next().ConfigureAwait(false);
            return;
        }

        if (policy.Category == RateLimitCategory.M)
            await AuditBlockedAsync(context, policy, tenantId, userId).ConfigureAwait(false);

        await WriteRateLimitResponseAsync(context, policy, verdict).ConfigureAwait(false);
    }

    /// <summary>
    /// Auditoria RateLimit hallazgo #6 — el fail-open sobre claims faltantes (doc-comment de la
    /// clase) sigue siendo el comportamiento correcto para rutas anónimas protegidas por otro
    /// mecanismo, pero antes de esto era completamente silencioso: nada distinguía "tráfico
    /// anónimo esperado" de "endpoint autenticado mal configurado sin tenant_id/sub". Este counter
    /// (distinto de <c>fallback_open_total</c> — no es una falla de infraestructura, es una señal
    /// de configuración) permite alertar si un endpoint que debería tener claims deja de tenerlos.
    /// </summary>
    private static void RecordMissingClaims(IServiceProvider services, string policyName) =>
        services.GetService<RateLimitMetrics>()?.RecordMissingClaims(policyName);

    /// <summary>Invariante §4 del plan — categoría M exige rastro de auditoría incluso al 429. Se
    /// atrapa cualquier falla del sink (no debe romper la respuesta 429 que sí importa devolver al
    /// cliente) — solo se loguea, mismo criterio de "no dejar que observabilidad tumbe una request"
    /// que ya usa <c>RateLimitMetrics</c>.</summary>
    private static async Task AuditBlockedAsync(
        ResourceExecutingContext context,
        RateLimitPolicyDefinition policy,
        Guid tenantId,
        Guid userId
    )
    {
        var services = context.HttpContext.RequestServices;
        try
        {
            var sink = services.GetRequiredService<IRateLimitAuditSink>();
            var correlation = services.GetService<ICorrelationContext>();
            await sink.OnBlockedAsync(
                    new RateLimitAuditContext(
                        tenantId,
                        userId,
                        policy.Name.Value,
                        context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                        context.HttpContext.Request.Headers.UserAgent.ToString(),
                        correlation?.CorrelationId
                    ),
                    context.HttpContext.RequestAborted
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            services
                .GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(RateLimitAttribute).FullName!)
                .LogWarning(
                    ex,
                    "IRateLimitAuditSink failed for policy {Policy} — 429 still returned.",
                    policy.Name.Value
                );
        }
    }

    /// <summary>Formato de respuesta exacto de Plan_Implementacion_Fases.md §6.3 — headers + body camelCase.
    /// Escribe el JSON directo al body (mismo patrón que <c>ExceptionHandlingMiddleware</c>) en vez
    /// de devolver un <see cref="ObjectResult"/>: una acción con <c>[Produces("text/csv")]</c> (p.ej.
    /// un export) restringe la negociación de contenido de CUALQUIER <c>ObjectResult</c> que
    /// devuelva — incluido este body de error — y el JSON nunca encuentra un formatter de CSV (406
    /// NotAcceptable en vez de 429). Escribir directo evita depender de qué formatters/atributos de
    /// content-type tenga la acción.</summary>
    private static async Task WriteRateLimitResponseAsync(
        ResourceExecutingContext context,
        RateLimitPolicyDefinition policy,
        RateLimitVerdict verdict
    )
    {
        var response = context.HttpContext.Response;
        var resetAtUnixSeconds = DateTimeOffset.UtcNow.AddSeconds(verdict.RetryAfterSeconds).ToUnixTimeSeconds();

        response.Headers["Retry-After"] = verdict.RetryAfterSeconds.ToString();
        response.Headers["X-RateLimit-Policy"] = policy.Name.Value;
        response.Headers["X-RateLimit-Layer"] = verdict.Layer;
        response.Headers["X-RateLimit-Limit"] = verdict.Limit.ToString();
        response.Headers["X-RateLimit-Remaining"] = "0";
        response.Headers["X-RateLimit-Reset"] = resetAtUnixSeconds.ToString();

        response.StatusCode = StatusCodes.Status429TooManyRequests;
        await response
            .WriteAsJsonAsync(
                new
                {
                    Code = "RateLimit.Exceeded",
                    Message = $"{verdict.Layer} rate limit exceeded. Retry after {verdict.RetryAfterSeconds} seconds.",
                    Policy = policy.Name.Value,
                    Layer = verdict.Layer,
                },
                context.HttpContext.RequestAborted
            )
            .ConfigureAwait(false);

        context.Result = new EmptyResult();
    }
}
