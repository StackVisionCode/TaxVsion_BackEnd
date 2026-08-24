using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Onboarding.Admin.Commands;
using TaxVision.Auth.Application.Onboarding.Admin.Queries;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Roles;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>PayFlow (Fase 17) — administración de onboardings en ManualReview/ProvisioningFailed.
/// PlatformAdmin-only: el tenant todavía no existe en la mayoría de los casos, "cross-tenant" ni
/// siquiera aplica acá.</summary>
[ApiController]
[Route("auth/onboarding/admin")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
[HasPermission(PermissionCatalog.OnboardingAdminManage)]
public sealed class OnboardingAdminController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimit("auth.f.onboarding_admin_read")]
    [ProducesResponseType<PagedResult<OnboardingAdminSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] TenantOnboardingStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        CancellationToken ct = default
    )
    {
        var result = await bus.InvokeAsync<Result<PagedResult<OnboardingAdminSummaryResponse>>>(
            new GetOnboardingsAdminQuery(status, page, limit),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [RateLimit("auth.f.onboarding_admin_read")]
    [ProducesResponseType<OnboardingAdminDetailResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<OnboardingAdminDetailResponse>>(
            new GetOnboardingAdminDetailQuery(id),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/resume")]
    [RateLimit("auth.g.onboarding_admin_manage")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new ResumeOnboardingAdminCommand(id), ct);
        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record UpdateAndResumeRequest(string? Subdomain, Guid? PlanId);

    [HttpPost("{id:guid}/update-and-resume")]
    [RateLimit("auth.g.onboarding_admin_manage")]
    public async Task<IActionResult> UpdateAndResume(
        Guid id,
        [FromBody] UpdateAndResumeRequest request,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result>(
            new UpdateAndResumeOnboardingAdminCommand(id, request.Subdomain, request.PlanId),
            ct
        );
        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ForceCompleteRequest(string Reason);

    [HttpPost("{id:guid}/force-complete")]
    [RateLimit("auth.g.onboarding_admin_manage")]
    public async Task<IActionResult> ForceComplete(
        Guid id,
        [FromBody] ForceCompleteRequest request,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result>(new ForceCompleteOnboardingAdminCommand(id, request.Reason), ct);
        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/resend-receipt")]
    [RateLimit("auth.g.onboarding_admin_manage")]
    public async Task<IActionResult> ResendReceipt(Guid id, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new ResendOnboardingReceiptAdminCommand(id), ct);
        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CancelAndRefundRequest(string Reason, string Confirmation);

    [HttpPost("{id:guid}/cancel-and-refund")]
    // Categoría M (dispara reembolso Stripe real) — ver AuthOnboardingAdminCancelRefund.
    [RateLimit("auth.m.onboarding_admin_cancel_refund")]
    public async Task<IActionResult> CancelAndRefund(
        Guid id,
        [FromBody] CancelAndRefundRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var adminUserId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new CancelAndRefundOnboardingAdminCommand(id, request.Reason, request.Confirmation, adminUserId),
            ct
        );
        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
