using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Terms;

namespace TaxVision.Auth.Api.Middleware;

/// <summary>
/// Fase L1.4 — bloquea (409) requests autenticadas de un tenant que no acepto la
/// version vigente del ToS/AUP (TermsOptions.CurrentVersion). Corre despues de
/// UseAuthentication/UseAuthorization, igual que SessionDenylistMiddleware — solo
/// actua si context.User trae un tenant_id real (los tokens M2M no lo tienen, asi
/// que el trafico entre microservicios nunca se ve afectado).
/// </summary>
public sealed class TermsAcceptanceMiddleware(RequestDelegate next)
{
    private static readonly string[] ExemptPathPrefixes =
    [
        "/health",
        "/auth/service-token",
        "/auth/.well-known",
        "/openapi",
        "/swagger",
        // El propio endpoint de aceptacion no puede quedar bloqueado por si mismo.
        "/auth/tenant/terms",
    ];

    public async Task InvokeAsync(
        HttpContext context,
        ITenantTermsAcceptanceRepository acceptances,
        IOptions<TermsOptions> options
    )
    {
        if (
            ExemptPathPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix))
            || context.User.Identity is not { IsAuthenticated: true }
            || !context.User.TryGetTenantId(out var tenantId)
        )
        {
            await next(context);
            return;
        }

        var latest = await acceptances.GetLatestAsync(tenantId, context.RequestAborted);
        if (latest?.TermsVersion == options.Value.CurrentVersion)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(
            new
            {
                type = "Terms.NotAccepted",
                title = "The current Terms of Service/Acceptable Use Policy has not been accepted yet.",
                currentVersion = options.Value.CurrentVersion,
            }
        );
    }
}
