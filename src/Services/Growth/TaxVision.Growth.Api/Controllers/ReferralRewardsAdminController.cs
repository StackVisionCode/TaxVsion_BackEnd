using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Admin;
using TaxVision.Referrals.Application.Rewards.ReviewRewardCase;
using Wolverine;
using ActorType = BuildingBlocks.ActorTypeAuthorization.ActorType;
using AllowActorTypesAttribute = BuildingBlocks.Web.ActorTypeAuthorization.AllowActorTypesAttribute;
using HasPermissionAttribute = BuildingBlocks.Web.ActorTypeAuthorization.HasPermissionAttribute;

namespace TaxVision.Growth.Api.Controllers;

/// <summary>
/// Revisión de premios de referidos (PlatformAdmin): ver la cola, retener un premio sospechoso (aún no acreditado),
/// darle el visto bueno o rechazarlo. Una oficina nunca llega aquí. El premio es de la oficina referidora: el comando
/// corre con SU tenant en contexto y el del llamador se restaura al terminar.
/// </summary>
[ApiController]
[Route("growth/referrals/admin/rewards")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class ReferralRewardsAdminController(IMessageBus bus) : ControllerBase
{
    public sealed record ReviewRequest(Guid TenantId, string Reason);

    [HttpGet]
    [RateLimit("growth.f.referral_read")]
    [HasPermission(GrowthPermissions.ReferralsRewardManage)]
    [ProducesResponseType<PagedResult<ReferralRewardAdminRow>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        CancellationToken ct,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20
    )
    {
        if (!this.TryGetTenantAndUser(out var callerTenant, out _) || callerTenant != PlatformTenant.Id)
            return Forbid();

        var result = await bus.InvokeAsync<Result<PagedResult<ReferralRewardAdminRow>>>(
            new ListReferralRewardsAdminQuery(status, page, size),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Retiene un premio aún no acreditado para revisarlo.</summary>
    [HttpPost("{rewardCaseId:guid}/flag")]
    [RateLimit("growth.g.referral_program_admin")]
    [HasPermission(GrowthPermissions.ReferralsRewardManage)]
    [ProducesResponseType<ReviewRewardCaseResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> Flag(Guid rewardCaseId, ReviewRequest request, CancellationToken ct) =>
        ReviewAsync(rewardCaseId, request, RewardReviewAction.Flag, ct);

    /// <summary>Visto bueno: el premio vuelve a la cola y se acredita al vencer su espera.</summary>
    [HttpPost("{rewardCaseId:guid}/release")]
    [RateLimit("growth.g.referral_program_admin")]
    [HasPermission(GrowthPermissions.ReferralsRewardManage)]
    [ProducesResponseType<ReviewRewardCaseResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> Release(Guid rewardCaseId, ReviewRequest request, CancellationToken ct) =>
        ReviewAsync(rewardCaseId, request, RewardReviewAction.Release, ct);

    /// <summary>Rechazo: el premio se cancela y no se acredita.</summary>
    [HttpPost("{rewardCaseId:guid}/reject")]
    [RateLimit("growth.g.referral_program_admin")]
    [HasPermission(GrowthPermissions.ReferralsRewardManage)]
    [ProducesResponseType<ReviewRewardCaseResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> Reject(Guid rewardCaseId, ReviewRequest request, CancellationToken ct) =>
        ReviewAsync(rewardCaseId, request, RewardReviewAction.Reject, ct);

    private async Task<IActionResult> ReviewAsync(
        Guid rewardCaseId,
        ReviewRequest request,
        RewardReviewAction action,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var callerTenant, out var actorId) || callerTenant != PlatformTenant.Id)
            return Forbid();
        if (request.TenantId == Guid.Empty)
            return BadRequest(
                new Error("ReferralReward.InvalidTenant", "The office that owns the reward is required.")
            );

        var caller = bus.TenantId;
        bus.TenantId = request.TenantId.ToString();
        try
        {
            var result = await bus.InvokeAsync<Result<ReviewRewardCaseResult>>(
                new ReviewRewardCaseCommand(request.TenantId, rewardCaseId, action, request.Reason, actorId),
                ct
            );
            return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
        }
        finally
        {
            bus.TenantId = caller;
        }
    }
}
