using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Commands;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>
/// Paso 3 de GRW-1: un commit tardío que Growth deja en revisión no corta la factura de onboarding.
/// Cualquier otro fallo de Growth sigue cortando para que Wolverine reintente.
/// </summary>
public sealed class OnboardingFinalizerTests
{
    private const string LateCommitPendingReviewCode = "Codes.CommitReservation.LateCommitPendingReview";

    [Fact]
    public async Task Finalize_GrowthLateCommitPendingReview_StillPublishesInvoiceRequested()
    {
        var late = Reservation(order: 0);
        var onTime = Reservation(order: 1);
        var growth = new ScriptedGrowthClient(
            new Dictionary<Guid, Error>
            {
                [late.CodeReservationId] = new(LateCommitPendingReviewCode, "Pending manual review."),
            }
        );
        var bus = new FakeMessageBus();

        var result = await NewFinalizer(growth).FinalizeAsync(Command(late, onTime), bus, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(2, growth.Commits.Count);
        var invoice = Assert.IsType<OnboardingInvoiceRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.True(invoice.Adjustments.Single(a => a.GrowthReservationId == late.CodeReservationId).PendingReview);
        Assert.False(invoice.Adjustments.Single(a => a.GrowthReservationId == onTime.CodeReservationId).PendingReview);
    }

    [Fact]
    public async Task Finalize_GrowthTransientFailure_ReturnsFailure()
    {
        var reservation = Reservation(order: 0);
        var growth = new ScriptedGrowthClient(
            new Dictionary<Guid, Error>
            {
                [reservation.CodeReservationId] = new("Growth.Unreachable", "Could not reach Growth."),
            }
        );
        var bus = new FakeMessageBus();

        var result = await NewFinalizer(growth).FinalizeAsync(Command(reservation), bus, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Growth.Unreachable", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    private static OnboardingFinalizer NewFinalizer(IGrowthOnboardingClient growth) =>
        new(growth, new FakeCorrelationContext(), NullLogger<OnboardingFinalizer>.Instance);

    private static FinalizeReservationDto Reservation(int order) =>
        new(Guid.NewGuid(), "Promo", $"CODE{order}", 500, "snapshot", order);

    private static OnboardingFinalizeCommand Command(params FinalizeReservationDto[] reservations) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Starter plan",
            "Payer",
            null,
            Guid.NewGuid(),
            GrossAmountCents: 5_000,
            DiscountAmountCents: reservations.Sum(r => r.DiscountCents),
            NetAmountCents: 5_000 - reservations.Sum(r => r.DiscountCents),
            "USD",
            "Mixed",
            ReferralAttributionId: null,
            reservations
        );

    /// <summary>Growth falso: el commit de cada reserva devuelve el error indicado o éxito.</summary>
    private sealed class ScriptedGrowthClient(IReadOnlyDictionary<Guid, Error> commitErrors) : IGrowthOnboardingClient
    {
        public List<Guid> Commits { get; } = [];

        public Task<Result> CommitAsync(
            Guid reservationId,
            Guid paymentReferenceId,
            string snapshotHash,
            Guid sourceEventId,
            string idempotencyKey,
            CancellationToken ct = default
        )
        {
            Commits.Add(reservationId);
            return Task.FromResult(
                commitErrors.TryGetValue(reservationId, out var error) ? Result.Failure(error) : Result.Success()
            );
        }

        public Task<Result<GrowthQuoteResult>> QuoteAsync(GrowthQuoteRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<GrowthReserveResult>> ReserveAsync(
            Guid quoteId,
            Guid paymentReferenceId,
            int ttlSeconds,
            string idempotencyKey,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> CancelAsync(
            Guid reservationId,
            Guid paymentReferenceId,
            string reason,
            string idempotencyKey,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> QualifyReferralAsync(GrowthQualifyRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
