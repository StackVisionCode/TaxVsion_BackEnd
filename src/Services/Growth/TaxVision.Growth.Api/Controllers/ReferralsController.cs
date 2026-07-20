using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Growth.Api.Common;
using TaxVision.Growth.Api.RateLimiting;
using TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;
using TaxVision.Referrals.Domain.Participants;
using Wolverine;

namespace TaxVision.Growth.Api.Controllers;

/// <summary>
/// Self-service tenant-to-tenant referral entry point. The referee identity always
/// comes from the validated JWT and cannot be supplied or overridden by the payload.
/// Taxpayer-to-taxpayer remains intentionally unavailable.
/// </summary>
[ApiController]
[Route("growth/referrals")]
[Authorize]
public sealed class ReferralsController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateAttributionRequest(Guid ProgramId, string ReferralCode)
    {
        public override string ToString() =>
            $"{nameof(CreateAttributionRequest)} {{ ProgramId = {ProgramId}, ReferralCode = <redacted> }}";
    }

    [HttpPost("attributions")]
    [EnableRateLimiting(GrowthRateLimitPolicies.ReferralAttribution)]
    [ProducesResponseType<CreateReferralAttributionResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAttribution(
        CreateAttributionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CreateReferralAttributionResult>>(
            new CreateReferralAttributionCommand(
                tenantId,
                request.ProgramId,
                request.ReferralCode,
                ReferralParticipantType.Tenant,
                tenantId,
                idempotencyKey,
                actorId
            ),
            ct
        );

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
