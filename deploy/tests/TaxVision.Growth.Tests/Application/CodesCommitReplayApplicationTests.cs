using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Reservations.CommitReservation;
using TaxVision.Codes.Application.Reservations.ReserveCode;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Application;

public sealed class CodesCommitReplayApplicationTests
{
    [Fact]
    public async Task Distinct_success_events_for_the_same_reservation_return_one_redemption()
    {
        var definition = GrowthTestData.CreateActivePercentageCode();
        var quote = GrowthTestData.CreateQuote(
            definition,
            GrowthTestData.RefereeTenantId,
            subjectId: "semantic-payment-replay"
        );
        var definitions = new InMemoryCodeDefinitionRepository(definition);
        var quotes = new InMemoryCodeQuoteRepository(quote);
        var reservations = new InMemoryCodeReservationRepository();
        var redemptions = new InMemoryCodeRedemptionRepository();
        var counters = new InMemoryCodeUsageCounterRepository();
        var idempotency = new FakeBusinessIdempotencyExecutor();
        var clock = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));
        var paymentId = Guid.NewGuid();

        var reserved = await ReserveCodeHandler.Handle(
            new ReserveCodeCommand(
                GrowthTestData.RefereeTenantId,
                quote.Id,
                "PaymentApp",
                paymentId,
                "reserve-semantic-replay",
                300
            ),
            definitions,
            quotes,
            reservations,
            counters,
            idempotency,
            clock,
            CancellationToken.None
        );
        Assert.True(reserved.IsSuccess);

        var reservation = Assert.Single(reservations.Reservations);
        var first = await CommitReservationHandler.Handle(
            new CommitReservationCommand(
                GrowthTestData.RefereeTenantId,
                reservation.Id,
                reservation.Payment.Source,
                reservation.Payment.PaymentId,
                reservation.SnapshotHash.Value,
                Guid.NewGuid(),
                "payment-success-event-1"
            ),
            definitions,
            reservations,
            redemptions,
            counters,
            new SuccessfulPaymentOutcomeVerifier(),
            idempotency,
            clock,
            CancellationToken.None
        );
        var second = await CommitReservationHandler.Handle(
            new CommitReservationCommand(
                GrowthTestData.RefereeTenantId,
                reservation.Id,
                reservation.Payment.Source,
                reservation.Payment.PaymentId,
                reservation.SnapshotHash.Value,
                Guid.NewGuid(),
                "payment-success-event-2"
            ),
            definitions,
            reservations,
            redemptions,
            counters,
            new SuccessfulPaymentOutcomeVerifier(),
            idempotency,
            clock,
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.RedemptionId, second.Value.RedemptionId);
        Assert.Single(redemptions.Redemptions);
        Assert.Equal(1, definition.CommittedRedemptions);
    }

    private sealed class SuccessfulPaymentOutcomeVerifier : IPaymentOutcomeVerifier
    {
        public Task<Result> VerifySucceededAsync(
            Guid tenantId,
            PaymentReference payment,
            Guid sourceEventId,
            SnapshotHash expectedSnapshotHash,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success());
    }
}
