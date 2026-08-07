using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Tenancy;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Domain.Audit;
using Wolverine;

namespace TaxVision.Auth.Api.Middleware;

/// <summary>
/// Resuelve el tenant candidato desde el Host de la request (Fase A3) y lo publica en
/// IResolvedTenantContext. Un Host que no resuelve a un tenant activo responde 404 —
/// nunca cae a un "tenant por defecto" — salvo en las rutas exentas (health checks y
/// los endpoints M2M/JWKS que otros servicios llaman directo por red interna, sin
/// pasar por el Gateway y por lo tanto sin un Host de tenant real).
/// Solo lee HttpContext.Request.Host — nunca X-Forwarded-Host directamente. La
/// confianza en ese header se resuelve antes, en ForwardedHeadersMiddleware, que solo
/// lo aplica cuando el origen inmediato está en la red de confianza configurada.
/// Resuelve desde el Host, antes e independientemente de la autenticación — a diferencia
/// de JwtTenantContextMiddleware, que resuelve del claim tenant_id del JWT ya verificado.
/// </summary>
public sealed class TenantHostResolutionMiddleware(
    RequestDelegate next,
    IOptions<TenantDomainOptions> options,
    ILogger<TenantHostResolutionMiddleware> logger
)
{
    /// <summary>
    /// Ventana de deduplicación del integration event. El audit log se escribe en CADA request
    /// (es el registro forense), pero al bus solo sale el primer fallo de cada host dentro de la
    /// ventana. Sin esto el middleware publica un evento por request a un exchange fanout con 17
    /// colas suscritas, y cada suscriptor lo persiste en su inbox durable antes de descubrir que
    /// no tiene handler: un host que no resuelve (el apex, un escáner, localhost en desarrollo)
    /// se multiplica por 17 escrituras a base de datos por request.
    /// </summary>
    private static readonly TimeSpan PublishDeduplicationWindow = TimeSpan.FromMinutes(5);

    private static readonly string[] ExemptPathPrefixes =
    [
        "/health",
        "/auth/service-token",
        "/auth/.well-known",
        "/openapi",
        "/swagger",
        // Fase A4 — llamables desde el apex (taxprocore.com), que nunca resuelve a un
        // tenant: alta de oficina (check-availability) y "encuentra tu oficina" por
        // email. "by-host" NO se exime a propósito: depende de que este middleware ya
        // haya resuelto el Host, es justo lo que ese endpoint expone al frontend.
        "/auth/subdomains/check-availability",
        "/auth/subdomains/reserve",
        "/auth/tenant-resolution/by-email",
    ];

    public async Task InvokeAsync(
        HttpContext context,
        ITenantResolver resolver,
        IResolvedTenantContext tenantContext,
        IAuthAuditWriter audit,
        IUnitOfWork unitOfWork,
        IRequestContext request,
        ICorrelationContext correlation,
        IMessageBus bus,
        IRateCounter rateCounter
    )
    {
        if (ExemptPathPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)))
        {
            await next(context);
            return;
        }

        var host = context.Request.Host.Host;
        var result = await resolver.ResolveAsync(host, context.RequestAborted);
        if (result.IsResolved)
        {
            tenantContext.SetResolvedTenant(result.TenantId);
            await next(context);
            return;
        }

        await RecordResolutionFailureAsync(
            host,
            result.FailureReason,
            audit,
            unitOfWork,
            request,
            correlation,
            bus,
            rateCounter,
            context.RequestAborted
        );

        if (options.Value.EnforceHostResolution)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Host desconocido o falsificado (X-Forwarded-Host ignorado, ver clase) es la
    /// señal de seguridad real de este middleware — se audita siempre, incluso en
    /// Development, para que un intento de Host Header Injection quede rastreable.
    /// </summary>
    private async Task RecordResolutionFailureAsync(
        string host,
        TenantResolutionFailureReason? reason,
        IAuthAuditWriter audit,
        IUnitOfWork unitOfWork,
        IRequestContext request,
        ICorrelationContext correlation,
        IMessageBus bus,
        IRateCounter rateCounter,
        CancellationToken ct
    )
    {
        await audit.AddAsync(
            AuthAuditLog.Record(
                PlatformTenant.Id,
                null,
                AuthAuditAction.TenantResolutionFailed,
                false,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Host",
                detailsJson: JsonSerializer.Serialize(new { host, reason = reason?.ToString() })
            ),
            ct
        );

        // Ademas del audit log (detalle forense completo), se publica como integration
        // event para que otros servicios (alertas/SIEM) puedan reaccionar sin tener que
        // leer la tabla de auditoria de Auth. Deduplicado por host: el audit log ya tiene
        // la cuenta exacta, y al bus le basta con saber que el host esta fallando.
        if (await IsFirstFailureInWindowAsync(host, rateCounter, ct))
        {
            await bus.PublishAsync(
                new TenantResolutionFailedIntegrationEvent
                {
                    TenantId = PlatformTenant.Id,
                    Host = host,
                    Reason = reason?.ToString() ?? "Unknown",
                    CorrelationId = correlation.CorrelationId,
                }
            );
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Un host distinto por request sigue produciendo un evento por request — el contador acota
    /// la repeticion del mismo host, no la cardinalidad de hosts que un atacante puede inventar.
    /// Si el contador no esta disponible se omite la publicacion en vez de propagar el fallo: el
    /// registro forense es el audit log, que ya quedo escrito, y ni tumbar la request ni volver a
    /// inundar el bus son intercambios aceptables por una señal que nadie consume en tiempo real.
    /// </summary>
    private async Task<bool> IsFirstFailureInWindowAsync(string host, IRateCounter rateCounter, CancellationToken ct)
    {
        var key = RateCounterKey.From($"auth:tenant-resolution-failed:{host.ToLowerInvariant()}");
        try
        {
            return await rateCounter.IncrementAndGetAsync(key, PublishDeduplicationWindow, ct) == 1;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "No se pudo deduplicar el evento de fallo de resolucion para {Host}; se omite la publicacion",
                host
            );
            return false;
        }
    }
}
