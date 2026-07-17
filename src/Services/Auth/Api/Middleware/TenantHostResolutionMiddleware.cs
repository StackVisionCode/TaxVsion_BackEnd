using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
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
/// No confundir con BuildingBlocks.Middleware.TenantResolutionMiddleware — ese lee el
/// header X-Tenant-Id ya propagado por el Gateway para requests autenticadas; este
/// resuelve desde el Host, antes/independiente de la autenticación.
/// </summary>
public sealed class TenantHostResolutionMiddleware(RequestDelegate next, IOptions<TenantDomainOptions> options)
{
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
        IMessageBus bus
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
    private static async Task RecordResolutionFailureAsync(
        string host,
        TenantResolutionFailureReason? reason,
        IAuthAuditWriter audit,
        IUnitOfWork unitOfWork,
        IRequestContext request,
        ICorrelationContext correlation,
        IMessageBus bus,
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
        // leer la tabla de auditoria de Auth — a proposito NO se publica un evento
        // equivalente "Succeeded" (este middleware corre en CADA request, inundaria el
        // bus sin aportar nada que el audit log local de accesos ya no cubra).
        await bus.PublishAsync(
            new TenantResolutionFailedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                Host = host,
                Reason = reason?.ToString() ?? "Unknown",
                CorrelationId = correlation.CorrelationId,
            }
        );

        await unitOfWork.SaveChangesAsync(ct);
    }
}
