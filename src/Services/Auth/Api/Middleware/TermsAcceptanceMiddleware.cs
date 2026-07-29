using BuildingBlocks.ActorTypeAuthorization;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;

namespace TaxVision.Auth.Api.Middleware;

/// <summary>
/// Fase L1.4 — bloquea (409) requests autenticadas de un tenant que no acepto la version vigente
/// del ToS/AUP. Corre despues de UseAuthentication/UseAuthorization, igual que
/// SessionDenylistMiddleware — solo actua si context.User trae un tenant_id real (los tokens M2M
/// clasicos no lo tienen, asi que el trafico entre microservicios nunca se ve afectado). PayFlow
/// (bug real encontrado en verificacion E2E): esa premisa dejo de ser cierta cuando
/// <c>OnboardingTokenClient</c>/<c>ReceiptDocumentClient</c> empezaron a pedir tokens M2M con
/// <see cref="TaxVision.Auth.Application.Onboarding.Abstractions.PlatformTenant.Id"/> como
/// sentinel de tenant (mismo patron que Documents Fase 10) — ese GUID no vacio hacia que
/// <c>TryGetTenantId</c> devolviera true para un token de servicio, y el middleware bloqueaba con
/// 409 "Terms.NotAccepted" cualquier llamada M2M que usara ese sentinel (PlatformTenant nunca
/// acepta ToS, no es un tenant real). Exento explicitamente por <c>ActorType.Service</c> antes de
/// mirar el tenant_id — el chequeo correcto de "es M2M", no una inferencia por la forma del claim.
///
/// PayFlow Fase 6 (retrofit): la version vigente se resuelve contra Onboarding.TermsVersions
/// (Kind=TermsOfService, Locale="en-US"), ya no contra TermsOptions.CurrentVersion — ver el
/// doc-comment de AcceptTermsHandler para el porque (TermsOptions quedo sin consumidores tras
/// este cambio, se deja la clase intacta por si se reintroduce un config-driven override).
/// </summary>
public sealed class TermsAcceptanceMiddleware(RequestDelegate next)
{
    private const string DefaultLocale = "en-US";

    private static readonly string[] ExemptPathPrefixes =
    [
        "/health",
        "/auth/service-token",
        "/auth/.well-known",
        "/openapi",
        "/swagger",
        // El propio endpoint de aceptacion no puede quedar bloqueado por si mismo.
        "/auth/tenant/terms",
        "/auth/onboarding/terms",
    ];

    public async Task InvokeAsync(
        HttpContext context,
        ITenantTermsAcceptanceRepository acceptances,
        ITermsVersionRepository termsVersions
    )
    {
        if (
            ExemptPathPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix))
            || context.User.Identity is not { IsAuthenticated: true }
            || context.User.GetActorType() == ActorType.Service
            || !context.User.TryGetTenantId(out var tenantId)
        )
        {
            await next(context);
            return;
        }

        var currentVersion = await termsVersions.GetCurrentAsync(
            TermsKind.TermsOfService,
            DefaultLocale,
            DateTime.UtcNow,
            context.RequestAborted
        );
        if (currentVersion is null)
        {
            await next(context);
            return;
        }

        var latest = await acceptances.GetLatestAsync(tenantId, context.RequestAborted);
        if (latest?.TermsVersion == currentVersion.Version)
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
                currentVersion = currentVersion.Version,
            }
        );
    }
}
