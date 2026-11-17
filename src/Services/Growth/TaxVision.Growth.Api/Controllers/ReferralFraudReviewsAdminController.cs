using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Growth.Api.IntegrationEvents;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Admin;
using TaxVision.Referrals.Application.Fraud.ResolveReview;
using TaxVision.Referrals.Application.Rewards;
using TaxVision.Referrals.Application.Rewards.GetReferralRewardNotice;
using Wolverine;
using ActorType = BuildingBlocks.ActorTypeAuthorization.ActorType;
using AllowActorTypesAttribute = BuildingBlocks.Web.ActorTypeAuthorization.AllowActorTypesAttribute;
using HasPermissionAttribute = BuildingBlocks.Web.ActorTypeAuthorization.HasPermissionAttribute;

namespace TaxVision.Growth.Api.Controllers;

/// <summary>
/// Revisiones de fraude de referidos (PlatformAdmin): la cola que abre el antifraude (mismo email de dueño o email
/// desechable) y su decisión. Aprobar libera el premio; rechazar lo cancela y le avisa al referidor. Usa el permiso de
/// revisión de premios, que es lo que se decide.
/// </summary>
[ApiController]
[Route("growth/referrals/admin/fraud-reviews")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class ReferralFraudReviewsAdminController(IMessageBus bus, ICorrelationContext correlation)
    : ControllerBase
{
    public sealed record ResolveRequest(string Reason);

    [HttpGet]
    [RateLimit("growth.f.referral_read")]
    [HasPermission(GrowthPermissions.ReferralsRewardManage)]
    [ProducesResponseType<PagedResult<ReferralFraudReviewAdminRow>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        CancellationToken ct,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20
    )
    {
        if (!this.TryGetTenantAndUser(out var callerTenant, out _) || callerTenant != PlatformTenant.Id)
            return Forbid();

        var result = await bus.InvokeAsync<Result<PagedResult<ReferralFraudReviewAdminRow>>>(
            new ListReferralFraudReviewsAdminQuery(status, page, size),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Visto bueno: el premio retenido vuelve a esperar su acreditación.</summary>
    [HttpPost("{reviewId:guid}/approve")]
    [RateLimit("growth.g.referral_program_admin")]
    [HasPermission(GrowthPermissions.ReferralsRewardManage)]
    [ProducesResponseType<ReferralFraudReviewResolution>(StatusCodes.Status200OK)]
    public Task<IActionResult> Approve(Guid reviewId, ResolveRequest request, CancellationToken ct) =>
        ResolveAsync(reviewId, request, approve: true, ct);

    /// <summary>Rechazo: el premio se cancela y el referidor recibe el aviso.</summary>
    [HttpPost("{reviewId:guid}/reject")]
    [RateLimit("growth.g.referral_program_admin")]
    [HasPermission(GrowthPermissions.ReferralsRewardManage)]
    [ProducesResponseType<ReferralFraudReviewResolution>(StatusCodes.Status200OK)]
    public Task<IActionResult> Reject(Guid reviewId, ResolveRequest request, CancellationToken ct) =>
        ResolveAsync(reviewId, request, approve: false, ct);

    private async Task<IActionResult> ResolveAsync(
        Guid reviewId,
        ResolveRequest request,
        bool approve,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var callerTenant, out var actorId) || callerTenant != PlatformTenant.Id)
            return Forbid();

        var result = await bus.InvokeAsync<Result<ReferralFraudReviewResolution>>(
            new ResolveReferralFraudReviewCommand(reviewId, approve, request.Reason, actorId),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        if (result.Value is { RewardCancelled: true, RewardCaseId: { } rewardCaseId, RewardTenantId: { } referrer })
            await NotifyCancelledAsync(referrer, rewardCaseId, ct);

        return Ok(result.Value);
    }

    // El aviso del premio es de la oficina referidora: corre con su tenant y se restaura el del llamador.
    private async Task NotifyCancelledAsync(Guid referrer, Guid rewardCaseId, CancellationToken ct)
    {
        var caller = bus.TenantId;
        bus.TenantId = referrer.ToString();
        try
        {
            var reward = await bus.InvokeAsync<Result<ReferralRewardNotice?>>(
                new GetReferralRewardNoticeQuery(referrer, rewardCaseId),
                ct
            );
            if (reward.IsSuccess && reward.Value is { } found)
                await bus.PublishAsync(
                    ReferralRewardEvents.Rejected(found, "FraudReviewRejected", correlation.CorrelationId)
                );
        }
        finally
        {
            bus.TenantId = caller;
        }
    }
}
