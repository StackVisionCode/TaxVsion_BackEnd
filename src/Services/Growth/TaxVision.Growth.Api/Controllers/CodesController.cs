using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Codes.Application.Definitions.ActivateCode;
using TaxVision.Codes.Application.Definitions.Common;
using TaxVision.Codes.Application.Definitions.CreateCodeDefinition;
using TaxVision.Codes.Application.Definitions.GetCodeDetails;
using TaxVision.Codes.Application.Definitions.RevokeCode;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Growth.Api.Authorization;
using TaxVision.Growth.Api.Common;
using Wolverine;

namespace TaxVision.Growth.Api.Controllers;

[ApiController]
[Route("growth/codes")]
[Authorize]
public sealed class CodesController(IMessageBus bus) : ControllerBase
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
    [HasPermission(GrowthPermissions.CodesManage)]
    [ProducesResponseType<CreateCodeDefinitionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(
        CreateCodeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!TryGetActor(out var tenantId, out var actorId))
            return Unauthorized();

        var ownership = ResolveOwnership(request, tenantId);
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
    [HasPermission(GrowthPermissions.CodesRead)]
    [ProducesResponseType<CodeDefinitionDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid codeDefinitionId, CancellationToken ct)
    {
        if (!TryGetActor(out var tenantId, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CodeDefinitionDetailsResponse>>(
            new GetCodeDetailsQuery(tenantId, codeDefinitionId, actorId),
            ct
        );

        return ToActionResult(result);
    }

    [HttpPost("{codeDefinitionId:guid}/activate")]
    [HasPermission(GrowthPermissions.CodesActivate)]
    [ProducesResponseType<CodeDefinitionStateResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(
        Guid codeDefinitionId,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!TryGetActor(out var tenantId, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CodeDefinitionStateResponse>>(
            new ActivateCodeCommand(tenantId, codeDefinitionId, actorId, idempotencyKey),
            ct
        );
        return ToActionResult(result);
    }

    [HttpPost("{codeDefinitionId:guid}/revoke")]
    [HasPermission(GrowthPermissions.CodesRevoke)]
    [ProducesResponseType<CodeDefinitionStateResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Revoke(
        Guid codeDefinitionId,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!TryGetActor(out var tenantId, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CodeDefinitionStateResponse>>(
            new RevokeCodeCommand(tenantId, codeDefinitionId, actorId, idempotencyKey),
            ct
        );
        return ToActionResult(result);
    }

    private (Guid OwnerTenantId, Guid? TenantScopeId)? ResolveOwnership(CreateCodeRequest request, Guid callerTenantId)
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

        if (request.TenantScopeId is not null && !User.HasPermission(GrowthPermissions.AdminCrossTenant))
            return null;

        return (PlatformTenant.Id, request.TenantScopeId);
    }

    private bool TryGetActor(out Guid tenantId, out Guid actorId)
    {
        actorId = Guid.Empty;
        return User.TryGetTenantId(out tenantId) && User.TryGetUserId(out actorId);
    }

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
