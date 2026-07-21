using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Growth.Api.Authorization;
using TaxVision.Growth.Api.Common;
using TaxVision.Referrals.Application.Qualifications.QualifyReferral;
using TaxVision.Referrals.Application.Rewards.ConfirmReferralRewardClawback;
using TaxVision.Referrals.Application.Rewards.ConfirmReferralRewardGrant;
using TaxVision.Referrals.Domain.Programs;
using Wolverine;

namespace TaxVision.Growth.Api.Controllers;

/// <summary>
/// M2M callbacks. These routes deliberately do not match the public /growth Gateway
/// route. Tenant and service identity are always taken from the validated service JWT.
/// </summary>
[ApiController]
[Route("internal/referrals")]
[Authorize]
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

    public sealed record ConfirmGrantRequest(Guid AttemptId, string MaterializedBenefitReference);

    public sealed record ConfirmClawbackRequest(Guid AttemptId, string ReversalReference);

    [HttpPost("qualifications")]
    [HasServiceScope(GrowthServiceScopes.ReferralsQualify)]
    [ProducesResponseType<QualifyReferralResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Qualify(
        QualifyReferralRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!TryGetServiceActor(out var tenantId, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<QualifyReferralResult>>(
            new QualifyReferralCommand(
                tenantId,
                request.AttributionId,
                request.QualifyingEventId,
                request.PaymentId,
                request.PaymentSource,
                request.PaymentAmountCents,
                request.PaymentCurrency,
                request.IsFirstSuccessfulPayment,
                request.PaymentSucceededAtUtc,
                idempotencyKey,
                actorId
            ),
            ct
        );

        return ToActionResult(result);
    }

    [HttpPost("grants/{grantId:guid}/confirm")]
    [HasServiceScope(GrowthServiceScopes.ReferralsRewardConfirm)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmGrant(
        Guid grantId,
        ConfirmGrantRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!TryGetServiceActor(out var tenantId, out var actorId))
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmClawback(
        Guid grantId,
        ConfirmClawbackRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!TryGetServiceActor(out var tenantId, out var actorId))
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

    private bool TryGetServiceActor(out Guid tenantId, out Guid actorId)
    {
        actorId = Guid.Empty;
        return User.TryGetTenantId(out tenantId) && User.TryGetUserId(out actorId);
    }

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
