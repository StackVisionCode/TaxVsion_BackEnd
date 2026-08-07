using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Invitations.TokenReferences.Commands;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>Fase 18 — M2M-only: Tenant deposita acá el raw token de activación del TenantAdmin antes
/// de publicar TenantCreatedIntegrationEvent, para nunca mandar el raw token por RabbitMQ (mismo
/// patrón TokenReference que Onboarding, Fase 9). One-shot, TTL 30s — TenantCreatedConsumer lo
/// consume in-process en el mismo request de procesamiento del evento.</summary>
[ApiController]
[Route("internal/invitations/token-references")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalInvitationTokenReferencesController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [RateLimitExempt(
        "M2M ServiceOnly (Fase 18) — tráfico servicio-a-servicio desde Tenant, nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<StoreInvitationTokenReferenceResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Store(StoreInvitationTokenReferenceCommand command, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<StoreInvitationTokenReferenceResponse>>(command, ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
