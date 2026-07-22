using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Codes.Application.Compensations.CompensateRedemption;
using TaxVision.Codes.Application.Quotes.CreateQuote;
using TaxVision.Codes.Application.Quotes.CreateSystemQuote;
using TaxVision.Codes.Application.Reservations.CancelReservation;
using TaxVision.Codes.Application.Reservations.CommitReservation;
using TaxVision.Codes.Application.Reservations.ExpireReservation;
using TaxVision.Codes.Application.Reservations.ReserveCode;
using TaxVision.Codes.Domain.Compensations;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Api.Authorization;
using TaxVision.Growth.Api.Common;
using TaxVision.Growth.Api.RateLimiting;
using Wolverine;

namespace TaxVision.Growth.Api.Controllers;

/// <summary>
/// API M2M. Estas rutas no están bajo /growth y por diseño no coinciden con la
/// ruta pública configurada en Gateway.
/// </summary>
[ApiController]
[Route("internal/codes")]
[Authorize]
public sealed class InternalCodesController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateQuoteRequest(
        string CodeToken,
        SubjectType SubjectType,
        string SubjectId,
        string OfferOwner,
        string OfferId,
        string OfferVersion,
        long GrossAmountCents,
        string Currency,
        string SnapshotHash,
        int TtlSeconds,
        IReadOnlyCollection<CodeScopeTargetInput>? ScopeTargets
    )
    {
        public override string ToString() =>
            $"{nameof(CreateQuoteRequest)} {{ CodeToken = <redacted>, SubjectType = {SubjectType}, "
            + $"OfferId = {OfferId}, GrossAmountCents = {GrossAmountCents}, Currency = {Currency} }}";
    }

    public sealed record ReserveCodeRequest(Guid QuoteId, string PaymentSource, Guid PaymentId, int TtlSeconds);

    /// <summary>No plaintext code involved — the caller proves nothing beyond "I am an
    /// authorized service acting for this tenant" (the M2M JWT's tenant claim). Only ever
    /// resolves a Kind=BenefitGift code, never a regular user-redeemed promo/discount code.</summary>
    public sealed record ReserveBenefitGiftRequest(
        string OfferOwner,
        string OfferId,
        string OfferVersion,
        long GrossAmountCents,
        string Currency,
        string SnapshotHash,
        int QuoteTtlSeconds,
        string PaymentSource,
        Guid PaymentId,
        int ReservationTtlSeconds
    );

    /// <summary>Found=false is the common, non-error case: the tenant simply has no active
    /// benefit-gift code (never referred, or already spent it) — nothing to reserve.</summary>
    public sealed record ReserveBenefitGiftResponse(
        bool Found,
        Guid? CodeReservationId,
        Guid? CodeDefinitionId,
        long? GrossAmountCents,
        long? DiscountAmountCents,
        long? NetAmountCents,
        string? Currency,
        DateTime? ExpiresAtUtc
    )
    {
        public static readonly ReserveBenefitGiftResponse NotFound = new(false, null, null, null, null, null, null, null);
    }

    public sealed record CommitReservationRequest(
        string PaymentSource,
        Guid PaymentId,
        string SnapshotHash,
        Guid SourceEventId
    );

    public sealed record CancelReservationRequest(string PaymentSource, Guid PaymentId, string Reason);

    public sealed record CompensateRedemptionRequest(
        CodeCompensationType Type,
        long AdjustmentAmountCents,
        string Currency,
        string Reason,
        Guid SourceEventId
    );

    [HttpPost("quotes")]
    [HasServiceScope(GrowthServiceScopes.CodesQuote)]
    [EnableRateLimiting(GrowthRateLimitPolicies.CodeQuote)]
    [ProducesResponseType<CreateQuoteResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Quote(
        CreateQuoteRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CreateQuoteResponse>>(
            new CreateQuoteCommand(
                tenantId,
                request.CodeToken,
                request.SubjectType,
                request.SubjectId,
                request.OfferOwner,
                request.OfferId,
                request.OfferVersion,
                request.GrossAmountCents,
                request.Currency,
                request.SnapshotHash,
                idempotencyKey,
                request.TtlSeconds,
                request.ScopeTargets
            ),
            ct
        );
        return ToActionResult(result);
    }

    /// <summary>Composes <see cref="CreateSystemQuoteCommand"/> + the existing
    /// <see cref="ReserveCodeCommand"/> into one atomic-from-the-caller's-view call — Subscription
    /// (or any future caller) doesn't need to know quotes and reservations are two steps.
    /// Each sub-command gets its own idempotency key, suffixed off the request's, so a retry of
    /// the whole call safely re-plays both steps without double-reserving.</summary>
    [HttpPost("benefit-gifts/reserve")]
    [HasServiceScope(GrowthServiceScopes.CodesReserveBenefitGift)]
    [EnableRateLimiting(GrowthRateLimitPolicies.CodeQuote)]
    [ProducesResponseType<ReserveBenefitGiftResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReserveBenefitGift(
        ReserveBenefitGiftRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var quote = await bus.InvokeAsync<Result<CreateQuoteResponse>>(
            new CreateSystemQuoteCommand(
                tenantId,
                request.OfferOwner,
                request.OfferId,
                request.OfferVersion,
                request.GrossAmountCents,
                request.Currency,
                request.SnapshotHash,
                $"{idempotencyKey}:quote",
                request.QuoteTtlSeconds
            ),
            ct
        );

        if (quote.IsFailure)
        {
            return quote.Error.Code == "Codes.CreateSystemQuote.NoActiveBenefit"
                ? Ok(ReserveBenefitGiftResponse.NotFound)
                : StatusCode(quote.Error.ToHttpStatusCode(), quote.Error);
        }

        var reservation = await bus.InvokeAsync<Result<ReserveCodeResponse>>(
            new ReserveCodeCommand(
                tenantId,
                quote.Value.QuoteId,
                request.PaymentSource,
                request.PaymentId,
                $"{idempotencyKey}:reserve",
                request.ReservationTtlSeconds
            ),
            ct
        );
        if (reservation.IsFailure)
            return StatusCode(reservation.Error.ToHttpStatusCode(), reservation.Error);

        return Ok(
            new ReserveBenefitGiftResponse(
                Found: true,
                reservation.Value.ReservationId,
                reservation.Value.CodeDefinitionId,
                reservation.Value.GrossAmountCents,
                reservation.Value.DiscountAmountCents,
                reservation.Value.NetAmountCents,
                reservation.Value.Currency,
                reservation.Value.ExpiresAtUtc
            )
        );
    }

    [HttpPost("reservations")]
    [HasServiceScope(GrowthServiceScopes.CodesReserve)]
    [ProducesResponseType<ReserveCodeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reserve(
        ReserveCodeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReserveCodeResponse>>(
            new ReserveCodeCommand(
                tenantId,
                request.QuoteId,
                request.PaymentSource,
                request.PaymentId,
                idempotencyKey,
                request.TtlSeconds
            ),
            ct
        );
        return ToActionResult(result);
    }

    [HttpPost("reservations/{reservationId:guid}/commit")]
    [HasServiceScope(GrowthServiceScopes.CodesCommit)]
    [ProducesResponseType<CommitReservationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Commit(
        Guid reservationId,
        CommitReservationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CommitReservationResponse>>(
            new CommitReservationCommand(
                tenantId,
                reservationId,
                request.PaymentSource,
                request.PaymentId,
                request.SnapshotHash,
                request.SourceEventId,
                idempotencyKey
            ),
            ct
        );
        return ToActionResult(result);
    }

    [HttpPost("reservations/{reservationId:guid}/cancel")]
    [HasServiceScope(GrowthServiceScopes.CodesCancel)]
    [ProducesResponseType<CancelReservationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(
        Guid reservationId,
        CancelReservationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CancelReservationResponse>>(
            new CancelReservationCommand(
                tenantId,
                reservationId,
                request.PaymentSource,
                request.PaymentId,
                request.Reason,
                idempotencyKey
            ),
            ct
        );
        return ToActionResult(result);
    }

    [HttpPost("reservations/{reservationId:guid}/expire")]
    [HasServiceScope(GrowthServiceScopes.CodesCancel)]
    [ProducesResponseType<ExpireReservationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Expire(
        Guid reservationId,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ExpireReservationResponse>>(
            new ExpireReservationCommand(tenantId, reservationId, idempotencyKey),
            ct
        );
        return ToActionResult(result);
    }

    [HttpPost("redemptions/{redemptionId:guid}/compensate")]
    [HasServiceScope(GrowthServiceScopes.CodesCompensate)]
    [ProducesResponseType<CompensateRedemptionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Compensate(
        Guid redemptionId,
        CompensateRedemptionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CompensateRedemptionResponse>>(
            new CompensateRedemptionCommand(
                tenantId,
                redemptionId,
                request.Type,
                request.AdjustmentAmountCents,
                request.Currency,
                request.Reason,
                request.SourceEventId,
                idempotencyKey
            ),
            ct
        );
        return ToActionResult(result);
    }

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
