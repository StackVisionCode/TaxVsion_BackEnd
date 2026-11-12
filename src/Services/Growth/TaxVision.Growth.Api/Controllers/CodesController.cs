using BuildingBlocks.Authorization;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Codes.Application.Definitions.ActivateCode;
using TaxVision.Codes.Application.Definitions.Admin;
using TaxVision.Codes.Application.Definitions.Common;
using TaxVision.Codes.Application.Definitions.CreateCodeDefinition;
using TaxVision.Codes.Application.Definitions.GetCodeDetails;
using TaxVision.Codes.Application.Definitions.RevokeCode;
using TaxVision.Codes.Application.Definitions.SendGiftcard;
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
/// Administración de giftcards y códigos promocionales. REGLA DE PRODUCTO: solo la compañía (PlatformAdmin) las
/// crea y administra —afectan suscripciones y planes—; las oficinas (tenants) solo las canjean escribiendo el código
/// en el alta. AdminCrossTenant (código de plataforma acotado a un tenant) es un chequeo adicional en código.
/// </summary>
[ApiController]
[Route("growth/codes")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class CodesController(
    IMessageBus bus,
    BuildingBlocks.Web.ActorTypeAuthorization.IUserPermissionsSource permissionsSource,
    IConfiguration configuration,
    BuildingBlocks.Common.ICorrelationContext correlation
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

    /// <summary>Giftcards de plataforma (más recientes primero), con su beneficio, vigencia, topes y canjes.</summary>
    [HttpGet]
    [RateLimit("growth.f.codes_read")]
    [HasPermission(GrowthPermissions.CodesRead)]
    [ProducesResponseType<BuildingBlocks.Common.PagedResult<CodeDefinitionDetailsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        [FromQuery] string? status = null
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _) || tenantId != PlatformTenant.Id)
            return Forbid();

        var result = await bus.InvokeAsync<Result<BuildingBlocks.Common.PagedResult<CodeDefinitionDetailsResponse>>>(
            new ListPlatformCodesQuery(page, size, status),
            ct
        );
        return ToActionResult(result);
    }

    /// <summary>Quién canjeó la giftcard, cuándo y cuánto se descontó.</summary>
    public sealed record SendGiftcardRequest(
        string Code,
        string RecipientEmail,
        string? RecipientName,
        string? PersonalMessage
    )
    {
        public override string ToString() =>
            $"{nameof(SendGiftcardRequest)} {{ Code = <redacted>, RecipientEmail = <redacted> }}";
    }

    public sealed record SendGiftcardResponse(string Status);

    /// <summary>
    /// Envía la giftcard por correo desde la plataforma. El backend solo guarda el hash del código: el administrador
    /// lo reenvía y se verifica contra esta giftcard. El correo lo arma Notification a partir de un evento de
    /// integración (sin llamadas M2M); aquí solo se valida y se publica.
    /// </summary>
    [HttpPost("{codeDefinitionId:guid}/send")]
    [RateLimit("growth.g.codes_create")]
    [HasPermission(GrowthPermissions.CodesManage)]
    [ProducesResponseType<SendGiftcardResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Send(Guid codeDefinitionId, SendGiftcardRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId) || tenantId != PlatformTenant.Id)
            return Forbid();

        var receipt = await bus.InvokeAsync<Result<SendGiftcardReceipt>>(
            new SendGiftcardCommand(
                codeDefinitionId,
                request.Code,
                request.RecipientEmail,
                request.RecipientName,
                request.PersonalMessage,
                actorId
            ),
            ct
        );
        if (receipt.IsFailure)
            return StatusCode(receipt.Error.ToHttpStatusCode(), receipt.Error);

        var landing = (configuration["Giftcards:RedeemBaseUrl"] ?? "http://localhost:4201").TrimEnd('/');
        var value = receipt.Value;
        await bus.PublishAsync(
            new GiftcardShareRequestedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                CorrelationId = correlation.CorrelationId,
                AggregateId = codeDefinitionId,
                RecipientEmail = value.RecipientEmail,
                RecipientName = value.RecipientName,
                ValueLabel = value.ValueLabel,
                Code = value.Code,
                ExpiresAtUtc = value.ExpiresAtUtc,
                PersonalMessage = value.PersonalMessage,
                RedeemUrl = $"{landing}/register?gift={Uri.EscapeDataString(value.Code)}",
            }
        );

        return Accepted(new SendGiftcardResponse("Queued"));
    }

    [HttpGet("{codeDefinitionId:guid}/redemptions")]
    [RateLimit("growth.f.codes_read")]
    [HasPermission(GrowthPermissions.CodesRedemptionRead)]
    [ProducesResponseType<BuildingBlocks.Common.PagedResult<CodeRedemptionRow>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRedemptions(
        Guid codeDefinitionId,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _) || tenantId != PlatformTenant.Id)
            return Forbid();

        var result = await bus.InvokeAsync<Result<BuildingBlocks.Common.PagedResult<CodeRedemptionRow>>>(
            new ListCodeRedemptionsQuery(codeDefinitionId, page, size),
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
