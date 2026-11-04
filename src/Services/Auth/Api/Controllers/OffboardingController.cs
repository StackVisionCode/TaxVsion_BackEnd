using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Offboarding;
using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Pre-flight descubrible del retiro: qué servicios exponen <c>offboarding-impact</c> y cómo llamarlos, para que
/// el cliente arme el diálogo sin una lista a mano. Sale del registro <see cref="OffboardingParticipants"/>,
/// que un fitness test mantiene alineado con las rutas reales.
/// </summary>
[ApiController]
[Route("auth/offboarding")]
public sealed class OffboardingController : ControllerBase
{
    /// <summary><c>impactPath</c> lleva <c>{userId}</c>; <c>acceptsEmail</c> = también <c>POST</c> con <c>{ "email" }</c>.</summary>
    public sealed record OffboardingParticipantResponse(
        string Service,
        string Module,
        string ImpactPath,
        bool AcceptsEmail
    );

    [HttpGet("participants")]
    [HasPermission(PermissionCatalog.UsersManage)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("auth.f.user_read")]
    [ProducesResponseType<IReadOnlyList<OffboardingParticipantResponse>>(StatusCodes.Status200OK)]
    public IActionResult Participants() =>
        Ok(
            OffboardingParticipants
                .All.Where(p => p.ImpactPath is not null)
                .Select(p => new OffboardingParticipantResponse(p.Service, p.Module, p.ImpactPath!, p.AcceptsEmail))
                .ToArray()
        );
}
