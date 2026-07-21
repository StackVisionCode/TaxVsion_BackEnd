using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using TaxVision.Codes.Application.Definitions.ActivateCode;
using TaxVision.Codes.Application.Definitions.Common;
using TaxVision.Codes.Application.Definitions.CreateCodeDefinition;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Growth.Api.Authorization;
using TaxVision.Growth.Api.Common;
using TaxVision.Growth.Api.RateLimiting;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;
using TaxVision.Referrals.Application.Codes.IssueTenantReferralCode;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;
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
public sealed class ReferralsController(
    IMessageBus bus,
    IReferralCodeTokenGenerator tokenGenerator,
    TimeProvider timeProvider,
    ILogger<ReferralsController> logger
) : ControllerBase
{
    public sealed record CreateAttributionRequest(Guid ProgramId, string ReferralCode)
    {
        public override string ToString() =>
            $"{nameof(CreateAttributionRequest)} {{ ProgramId = {ProgramId}, ReferralCode = <redacted> }}";
    }

    /// <summary>Null when the program has no referee benefit configured, or when issuing/
    /// activating it failed — the attribution itself already succeeded by this point and
    /// must never be rolled back or reported as failed just because the bonus discount
    /// (a secondary, best-effort side effect) could not be created.</summary>
    public sealed record RefereeBenefitInfo(
        Guid CodeDefinitionId,
        string DisplayPrefix,
        string LastFour,
        DateTime ExpiresAtUtc,
        string Code
    )
    {
        public override string ToString() =>
            $"{nameof(RefereeBenefitInfo)} {{ CodeDefinitionId = {CodeDefinitionId}, "
            + $"DisplayPrefix = {DisplayPrefix}, LastFour = {LastFour}, "
            + $"ExpiresAtUtc = {ExpiresAtUtc}, Code = <redacted> }}";
    }

    public sealed record CreateAttributionResponse(
        Guid AttributionId,
        string Status,
        bool WasReplay,
        RefereeBenefitInfo? RefereeBenefit
    )
    {
        public override string ToString() =>
            $"{nameof(CreateAttributionResponse)} {{ AttributionId = {AttributionId}, Status = {Status}, "
            + $"WasReplay = {WasReplay}, RefereeBenefit = {(RefereeBenefit is null ? "null" : "<redacted>")} }}";
    }

    public sealed record IssueCodeRequest(Guid ProgramId, DateTime ExpiresAtUtc);

    /// <summary>The clear-text code is computed here, at the delivery boundary, and never
    /// enters the idempotency-persisted <see cref="IssueTenantReferralCodeResult"/> — the
    /// generator is deterministic over (ProgramId, TenantId, IdempotencyKey), so recomputing
    /// it on every authenticated call to this endpoint is safe and lets a tenant who already
    /// has an active code retrieve it again without a one-time-reveal trap.</summary>
    public sealed record IssueCodeResponse(
        Guid ReferralCodeId,
        Guid ProgramId,
        string Status,
        string DisplayPrefix,
        string LastFour,
        DateTime ExpiresAtUtc,
        string ReferralCode
    )
    {
        public override string ToString() =>
            $"{nameof(IssueCodeResponse)} {{ ReferralCodeId = {ReferralCodeId}, ProgramId = {ProgramId}, "
            + $"Status = {Status}, DisplayPrefix = {DisplayPrefix}, LastFour = {LastFour}, "
            + $"ExpiresAtUtc = {ExpiresAtUtc}, ReferralCode = <redacted> }}";
    }

    [HttpPost("attributions")]
    [EnableRateLimiting(GrowthRateLimitPolicies.ReferralAttribution)]
    [ProducesResponseType<CreateAttributionResponse>(StatusCodes.Status200OK)]
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

        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        var attribution = result.Value;
        RefereeBenefitInfo? benefit = null;
        if (attribution.RefereeBenefitType is { } benefitType)
        {
            benefit = await TryIssueRefereeBenefitAsync(
                benefitType,
                attribution,
                request.ProgramId,
                tenantId,
                actorId,
                idempotencyKey,
                ct
            );
        }

        return Ok(
            new CreateAttributionResponse(
                attribution.AttributionId,
                attribution.Status.ToString(),
                attribution.WasReplay,
                benefit
            )
        );
    }

    /// <summary>Option B (opción B, decisión de negocio 2026-07-20): además del reward a quien
    /// refiere, el referido se lleva su propio CodeDefinition de descuento (Codes, no
    /// Referrals — Codes y Referrals no comparten aggregate, así que la única forma de conectar
    /// ambos bounded contexts es orquestando dos comandos separados desde acá, el mismo patrón
    /// que ya usa <c>PaymentSucceededConsumer</c>). Best-effort a propósito: si esto falla, la
    /// atribución YA se confirmó y no se revierte — un descuento de bienvenida perdido no debe
    /// bloquear el alta del referido. Reintentable: llamar de nuevo con el mismo
    /// Idempotency-Key reintenta solo esta parte (la atribución ya replay-safe la ignora).</summary>
    private async Task<RefereeBenefitInfo?> TryIssueRefereeBenefitAsync(
        ReferralRefereeBenefitType benefitType,
        CreateReferralAttributionResult attribution,
        Guid programId,
        Guid refereeTenantId,
        Guid actorId,
        string idempotencyKey,
        CancellationToken ct
    )
    {
        var benefitIdempotencyKey = $"referral-benefit:{idempotencyKey}";
        var generatedToken = tokenGenerator.Generate(programId, refereeTenantId, $"referee-benefit:{idempotencyKey}");
        if (generatedToken.IsFailure)
        {
            logger.LogWarning(
                "Could not generate a referee benefit token for attribution {AttributionId}: {Error}",
                attribution.AttributionId,
                generatedToken.Error.Code
            );
            return null;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var created = await bus.InvokeAsync<Result<CreateCodeDefinitionResponse>>(
            new CreateCodeDefinitionCommand(
                refereeTenantId,
                CodeOwnerScope.Tenant,
                refereeTenantId,
                "Referral welcome discount",
                CodeKind.BenefitGift,
                generatedToken.Value.Reveal(),
                benefitType == ReferralRefereeBenefitType.Percentage
                    ? CodeBenefitType.Percentage
                    : CodeBenefitType.FixedAmount,
                attribution.RefereeBenefitPercentageBasisPoints,
                attribution.RefereeBenefitFixedAmountCents,
                attribution.RefereeBenefitCurrency,
                MinimumPurchaseAmountCents: null,
                MinimumPurchaseCurrency: null,
                AllowStacking: false,
                nowUtc,
                nowUtc.AddDays(attribution.RefereeBenefitExpirationDays),
                MaxRedemptions: 1,
                MaxRedemptionsPerTenant: 1,
                MaxRedemptionsPerSubject: 1,
                Scopes: null,
                actorId,
                $"{benefitIdempotencyKey}:create"
            ),
            ct
        );
        if (created.IsFailure)
        {
            logger.LogWarning(
                "Could not create the referee benefit CodeDefinition for attribution {AttributionId}: {Error}",
                attribution.AttributionId,
                created.Error.Code
            );
            return null;
        }

        var activated = await bus.InvokeAsync<Result<CodeDefinitionStateResponse>>(
            new ActivateCodeCommand(
                refereeTenantId,
                created.Value.CodeDefinitionId,
                actorId,
                $"{benefitIdempotencyKey}:activate"
            ),
            ct
        );
        if (activated.IsFailure)
        {
            logger.LogWarning(
                "Could not activate the referee benefit CodeDefinition {CodeDefinitionId} for attribution {AttributionId}: {Error}",
                created.Value.CodeDefinitionId,
                attribution.AttributionId,
                activated.Error.Code
            );
            return null;
        }

        return new RefereeBenefitInfo(
            created.Value.CodeDefinitionId,
            created.Value.CodePrefix,
            created.Value.CodeLastFour,
            created.Value.ExpiresAtUtc ?? nowUtc,
            generatedToken.Value.Reveal()
        );
    }

    /// <summary>Idempotent get-or-create: calling again with the same Idempotency-Key for a
    /// tenant that already has an active code for this program returns that same code (still
    /// revealed in clear text — see <see cref="IssueCodeResponse"/>) instead of failing.</summary>
    [HttpPost("codes")]
    [HasPermission(GrowthPermissions.ReferralsOwnRead)]
    [ProducesResponseType<IssueCodeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> IssueCode(
        IssueCodeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IssueTenantReferralCodeResult>>(
            new IssueTenantReferralCodeCommand(
                tenantId,
                request.ProgramId,
                request.ExpiresAtUtc,
                actorId,
                idempotencyKey
            ),
            ct
        );

        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        var generated = tokenGenerator.Generate(request.ProgramId, tenantId, idempotencyKey);
        if (generated.IsFailure)
            return StatusCode(generated.Error.ToHttpStatusCode(), generated.Error);

        var value = result.Value;
        return Ok(
            new IssueCodeResponse(
                value.ReferralCodeId,
                value.ProgramId,
                value.Status.ToString(),
                value.DisplayPrefix,
                value.LastFour,
                value.ExpiresAtUtc,
                generated.Value.Reveal()
            )
        );
    }
}
