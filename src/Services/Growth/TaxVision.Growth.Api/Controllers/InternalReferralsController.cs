using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Growth.Api.Authorization;
using TaxVision.Growth.Api.Common;
using TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;
using TaxVision.Referrals.Application.Programs.GetActiveReferralProgram;
using TaxVision.Referrals.Application.Qualifications.QualifyReferral;
using TaxVision.Referrals.Application.Rewards.ConfirmReferralRewardClawback;
using TaxVision.Referrals.Application.Rewards.ConfirmReferralRewardGrant;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Shares;
using Wolverine;
using ActorType = BuildingBlocks.ActorTypeAuthorization.ActorType;
using AllowActorTypesAttribute = BuildingBlocks.Web.ActorTypeAuthorization.AllowActorTypesAttribute;

namespace TaxVision.Growth.Api.Controllers;

/// <summary>
/// M2M callbacks. These routes deliberately do not match the public /growth Gateway
/// route. Tenant and service identity are always taken from the validated service JWT.
/// </summary>
[ApiController]
[Route("internal/referrals")]
[Authorize]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalReferralsController(IMessageBus bus) : ControllerBase
{
    public sealed record QualifyReferralRequest(
        Guid AttributionId,
        Guid QualifyingEventId,
        Guid PaymentId,
        QualifyingPaymentSource PaymentSource,
        long PaymentAmountCents,
        string PaymentCurrency,
        bool IsFirstSuccessfulPayment,
        DateTime PaymentSucceededAtUtc
    );

    /// <summary>Atribución del alta (pre-tenant). El código de referido solo identifica quién recomendó: no cambia el precio.</summary>
    public sealed record OnboardingAttributionRequest(string ReferralCode, ReferralShareChannel? Channel, Guid? ShareId)
    {
        public override string ToString() =>
            $"{nameof(OnboardingAttributionRequest)} {{ ReferralCode = <redacted>, Channel = {Channel}, ShareId = {ShareId} }}";
    }

    public sealed record OnboardingAttributionResponse(Guid AttributionId, string Status);

    public sealed record ConfirmGrantRequest(Guid AttemptId, string MaterializedBenefitReference);

    public sealed record ConfirmClawbackRequest(Guid AttemptId, string ReversalReference);

    [HttpPost("grants/{grantId:guid}/confirm")]
    [HasServiceScope(GrowthServiceScopes.ReferralsRewardConfirm)]
    [RateLimit("growth.j.referrals_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmGrant(
        Guid grantId,
        ConfirmGrantRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ConfirmReferralRewardGrantCommand(
                tenantId,
                grantId,
                request.AttemptId,
                request.MaterializedBenefitReference,
                idempotencyKey,
                actorId
            ),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("grants/{grantId:guid}/clawbacks/confirm")]
    [HasServiceScope(GrowthServiceScopes.ReferralsRewardConfirm)]
    [RateLimit("growth.j.referrals_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmClawback(
        Guid grantId,
        ConfirmClawbackRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ConfirmReferralRewardClawbackCommand(
                tenantId,
                grantId,
                request.AttemptId,
                request.ReversalReference,
                idempotencyKey,
                actorId
            ),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
