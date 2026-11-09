using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Codes.Application.Definitions.ActivateCode;
using TaxVision.Codes.Application.Definitions.Common;
using TaxVision.Codes.Application.Definitions.CreateCodeDefinition;
using TaxVision.Codes.Application.Definitions.GetCodeDetails;
using TaxVision.Codes.Application.Definitions.RevokeCode;
using TaxVision.Codes.Application.Reservations.LateCommitReviews;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Growth.Api.Common;
using Wolverine;
using ActorType = BuildingBlocks.ActorTypeAuthorization.ActorType;
using AllowActorTypesAttribute = BuildingBlocks.Web.ActorTypeAuthorization.AllowActorTypesAttribute;
// Alias puntual (no `using BuildingBlocks.ActorTypeAuthorization;` completo) — ese namespace
// también trae ClaimsPrincipalExtensions.TryGetTenantId/TryGetUserId, que colisionarían
// (CS0121, ambiguo) con las mismas firmas de TaxVision.Growth.Api.Common (ClaimsPrincipalExtensions
// local de Growth, que este controller sigue usando para esas dos).
using HasPermissionAttribute = BuildingBlocks.Web.ActorTypeAuthorization.HasPermissionAttribute;

namespace TaxVision.Growth.Api.Controllers;

/// <summary>
/// Gestión de códigos de descuento por el propio tenant (staff). AdminCrossTenant (creación de
/// código de plataforma cross-tenant) es un chequeo adicional en código, no un actor type distinto
/// — sigue siendo un staff member (PlatformAdmin) el que llama.
/// </summary>
[ApiController]
[Route("growth/codes")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class CodesController(
    IMessageBus bus,
    BuildingBlocks.Web.ActorTypeAuthorization.IUserPermissionsSource permissionsSource
) : ControllerBase
{
    public sealed record CreateCodeRequest(
        CodeOwnerScope OwnerScope,
        Guid? TenantScopeId,
        string Name,
        CodeKind Kind,
        string CodeToken,
        CodeBenefitType BenefitType,
        int? PercentageBasisPoints,
        long? FixedAmountCents,
        string? FixedAmountCurrency,
        long? MinimumPurchaseAmountCents,
        string? MinimumPurchaseCurrency,
        bool AllowStacking,
        DateTime StartsAtUtc,
        DateTime? ExpiresAtUtc,
        long? MaxRedemptions,
        long? MaxRedemptionsPerTenant,
        long? MaxRedemptionsPerSubject,
        IReadOnlyCollection<CreateCodeScopeInput>? Scopes
    )
    {
        public override string ToString() =>
            $"{nameof(CreateCodeRequest)} {{ OwnerScope = {OwnerScope}, Name = {Name}, "
            + $"CodeToken = <redacted>, BenefitType = {BenefitType} }}";
    }

    [HttpPost]
    [RateLimit("growth.g.codes_create")]
    [HasPermission(GrowthPermissions.CodesManage)]
    [ProducesResponseType<CreateCodeDefinitionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(
        CreateCodeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Unauthorized();

        var ownership = await ResolveOwnershipAsync(request, tenantId, ct);
        if (ownership is null)
            return Forbid();

        var result = await bus.InvokeAsync<Result<CreateCodeDefinitionResponse>>(
            new CreateCodeDefinitionCommand(
                ownership.Value.OwnerTenantId,
                request.OwnerScope,
                ownership.Value.TenantScopeId,
                request.Name,
                request.Kind,
                request.CodeToken,
                request.BenefitType,
                request.PercentageBasisPoints,
                request.FixedAmountCents,
                request.FixedAmountCurrency,
                request.MinimumPurchaseAmountCents,
                request.MinimumPurchaseCurrency,
                request.AllowStacking,
                request.StartsAtUtc,
                request.ExpiresAtUtc,
                request.MaxRedemptions,
                request.MaxRedemptionsPerTenant,
                request.MaxRedemptionsPerSubject,
                request.Scopes,
                actorId,
                idempotencyKey
            ),
            ct
        );

        return ToActionResult(result);
    }

    [HttpGet("{codeDefinitionId:guid}")]
    [RateLimit("growth.f.codes_read")]
    [HasPermission(GrowthPermissions.CodesRead)]
    [ProducesResponseType<CodeDefinitionDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid codeDefinitionId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CodeDefinitionDetailsResponse>>(
            new GetCodeDetailsQuery(tenantId, codeDefinitionId, actorId),
            ct
        );

        return ToActionResult(result);
    }

    [HttpPost("{codeDefinitionId:guid}/activate")]
    [RateLimit("growth.g.codes_activate")]
    [HasPermission(GrowthPermissions.CodesActivate)]
    [ProducesResponseType<CodeDefinitionStateResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(
        Guid codeDefinitionId,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CodeDefinitionStateResponse>>(
            new ActivateCodeCommand(tenantId, codeDefinitionId, actorId, idempotencyKey),
            ct
        );
        return ToActionResult(result);
    }

    [HttpPost("{codeDefinitionId:guid}/revoke")]
    [RateLimit("growth.g.codes_revoke")]
    [HasPermission(GrowthPermissions.CodesRevoke)]
    [ProducesResponseType<CodeDefinitionStateResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Revoke(
        Guid codeDefinitionId,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CodeDefinitionStateResponse>>(
            new RevokeCodeCommand(tenantId, codeDefinitionId, actorId, idempotencyKey),
            ct
        );
        return ToActionResult(result);
    }

    /// <summary>Commits tardíos pendientes de revisión (GRW-1). Solo PlatformAdmin; la resolución es manual.</summary>
    [HttpGet("late-commit-reviews")]
    [AllowActorTypes(ActorType.PlatformAdmin)]
    [RateLimit("growth.f.codes_read")]
    [HasPermission(GrowthPermissions.AdminCrossTenant)]
    [ProducesResponseType<IReadOnlyList<LateCommitReviewResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPendingLateCommitReviews(CancellationToken ct, [FromQuery] int take = 50)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _) || tenantId != PlatformTenant.Id)
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<LateCommitReviewResponse>>>(
            new ListPendingLateCommitReviewsQuery(take),
            ct
        );
        return ToActionResult(result);
    }

    public sealed record ForceCommitLateReservationRequest(string? Note);

    /// <summary>Da por bueno un canje tardío (GRW-1). Solo prospera si PaymentApp confirma el pago; deja auditoría.</summary>
    [HttpPost("late-commit-reviews/{reviewId:guid}/force-commit")]
    [AllowActorTypes(ActorType.PlatformAdmin)]
    [RateLimit("growth.g.codes_revoke")]
    [HasPermission(GrowthPermissions.AdminCrossTenant)]
    [ProducesResponseType<ForceCommitLateReservationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForceCommitLateReservation(
        Guid reviewId,
        ForceCommitLateReservationRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId) || tenantId != PlatformTenant.Id)
            return Forbid();

        var result = await bus.InvokeAsync<Result<ForceCommitLateReservationResponse>>(
            new ForceCommitLateReservationCommand(reviewId, actorId, request.Note),
            ct
        );
        return ToActionResult(result);
    }

    private async Task<(Guid OwnerTenantId, Guid? TenantScopeId)?> ResolveOwnershipAsync(
        CreateCodeRequest request,
        Guid callerTenantId,
        CancellationToken ct
    )
    {
        if (request.OwnerScope == CodeOwnerScope.Tenant)
        {
            if (
                callerTenantId == PlatformTenant.Id
                || request.TenantScopeId is not null && request.TenantScopeId != callerTenantId
            )
                return null;

            return (callerTenantId, callerTenantId);
        }

        if (request.OwnerScope != CodeOwnerScope.Platform || callerTenantId != PlatformTenant.Id)
            return null;

        if (
            request.TenantScopeId is not null
            && !await permissionsSource.HasPermissionAsync(User, GrowthPermissions.AdminCrossTenant, ct)
        )
            return null;

        return (PlatformTenant.Id, request.TenantScopeId);
    }

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
