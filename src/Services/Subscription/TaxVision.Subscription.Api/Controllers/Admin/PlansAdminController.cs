using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Plans.Commands.CreateDraftVersion;
using TaxVision.Subscription.Application.Plans.Commands.CreatePlan;
using TaxVision.Subscription.Application.Plans.Commands.DeprecatePlan;
using TaxVision.Subscription.Application.Plans.Commands.PublishDraftVersion;
using TaxVision.Subscription.Application.Plans.Commands.ReplaceDraftVersion;
using TaxVision.Subscription.Application.Plans.Commands.SetPlanModules;
using TaxVision.Subscription.Application.Plans.Commands.SetPlanPrices;
using TaxVision.Subscription.Application.Plans.Commands.UpdatePlanMetadata;
using TaxVision.Subscription.Application.Plans.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

/// <summary>Autoría de planes — solo PlatformAdmin.</summary>
[ApiController]
[Route("admin/subscription/plans")]
[Authorize]
[HasPermission(SubscriptionPermissions.AdminCrossTenant)]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class PlansAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>CAT-F2.2 — lista completa de planes para la pantalla de autoría. Incluye TODAS las
    /// versiones (Draft/Published/Superseded/Archived) con features, entitlements y price tiers;
    /// el GET público <c>/subscription/plans</c> sólo expone la versión publicada.</summary>
    [HttpGet]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(typeof(IReadOnlyList<PlanAdminResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<PlanAdminResponse>>>(new GetPlansForAdminQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>CAT-F2.3 — crea un plan nuevo con versión v1 en Draft (sin módulos ni precios).
    /// El plan no sale en el listado público ni se puede contratar hasta que se publique su versión.</summary>
    [HttpPost]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(typeof(CreatePlanResult), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreatePlanRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CreatePlanResult>>(
            new CreatePlanCommand(
                request.Code,
                request.Name,
                request.Description,
                request.Tier,
                request.SupportedBillingCycles ?? [],
                request.TrialDaysDefault,
                userId
            ),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        return CreatedAtAction(nameof(List), null, result.Value);
    }

    /// <summary>CAT-F2.3 — edita Name/Description del plan. No cambia Code ni Tier ni versiones.</summary>
    [HttpPut("{planId:guid}")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateMetadata(
        Guid planId,
        [FromBody] UpdatePlanMetadataRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new UpdatePlanMetadataCommand(planId, request.Name, request.Description, userId),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Fija los módulos (module.*) del plan: publica una versión nueva (supersede la anterior)
    /// y encola el recálculo masivo de sus tenants.</summary>
    [HttpPut("{planId:guid}/modules")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SetModules(
        Guid planId,
        [FromBody] SetPlanModulesRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetPlanModulesCommand(planId, request.Modules ?? [], userId),
            ct
        );
        return result.IsSuccess ? Accepted() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>CAT-F2.4 — reemplaza el contenido completo de una versión Draft (módulos, features,
    /// entitlements, price tiers, trial, ciclos). Si la versión no es Draft devuelve 409: para cambiar
    /// una publicada hay que crear una versión nueva (<c>POST .../versions</c>, F2.5).</summary>
    [HttpPut("{planId:guid}/versions/{versionId:guid}/draft")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReplaceDraft(
        Guid planId,
        Guid versionId,
        [FromBody] ReplaceDraftVersionRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ReplaceDraftVersionCommand(
                planId,
                versionId,
                request.Modules ?? [],
                request.Features ?? [],
                request.Entitlements ?? [],
                request.PriceTiers ?? [],
                request.TrialDaysDefault,
                request.SupportedBillingCycles ?? [],
                userId
            ),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>CAT-F2.5 — abre una versión Draft nueva (v+1) para el plan. Solo una Draft a la vez.</summary>
    [HttpPost("{planId:guid}/versions")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(typeof(CreateDraftVersionResult), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateDraft(
        Guid planId,
        [FromBody] CreateDraftVersionRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CreateDraftVersionResult>>(
            new CreateDraftVersionCommand(
                planId,
                request.TrialDaysDefault,
                request.SupportedBillingCycles ?? [],
                userId
            ),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(List), null, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>CAT-F2.5 — publica una versión Draft; supersede la publicada anterior y encola el
    /// recálculo masivo de entitlements para los tenants del plan.</summary>
    [HttpPost("{planId:guid}/versions/{versionId:guid}/publish")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> PublishVersion(Guid planId, Guid versionId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new PublishDraftVersionCommand(planId, versionId, userId), ct);
        return result.IsSuccess ? Accepted() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>CAT-F2.5 — marca el plan como Deprecated (no se ofrece en nuevas contrataciones;
    /// tenants activos siguen).</summary>
    [HttpPost("{planId:guid}/deprecate")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deprecate(Guid planId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new DeprecatePlanCommand(planId, userId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>CAT-F2.5 — archiva un plan previamente deprecated.</summary>
    [HttpPost("{planId:guid}/archive")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid planId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ArchivePlanCommand(planId, userId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Revisa el precio mensual/anual del plan (publica una versión nueva que supersede la anterior).
    /// No cambia entitlements, así que no dispara recálculo.</summary>
    [HttpPut("{planId:guid}/prices")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPrices(
        Guid planId,
        [FromBody] SetPlanPricesRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetPlanPricesCommand(planId, request.MonthlyUsd, request.YearlyUsd, userId),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

public sealed record SetPlanModulesRequest(IReadOnlyList<string> Modules);

public sealed record SetPlanPricesRequest(decimal MonthlyUsd, decimal YearlyUsd);

public sealed record CreatePlanRequest(
    string Code,
    string Name,
    string Description,
    string Tier,
    IReadOnlyList<string>? SupportedBillingCycles,
    int TrialDaysDefault
);

public sealed record UpdatePlanMetadataRequest(string Name, string Description);

public sealed record ReplaceDraftVersionRequest(
    IReadOnlyList<string>? Modules,
    IReadOnlyList<DraftFeatureInput>? Features,
    IReadOnlyList<DraftEntitlementInput>? Entitlements,
    IReadOnlyList<DraftPriceTierInput>? PriceTiers,
    int TrialDaysDefault,
    IReadOnlyList<string>? SupportedBillingCycles
);

public sealed record CreateDraftVersionRequest(int TrialDaysDefault, IReadOnlyList<string>? SupportedBillingCycles);
