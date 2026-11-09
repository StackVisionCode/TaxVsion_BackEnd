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
            new InMemoryLateCommitReviewRepository(),
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
            new InMemoryLateCommitReviewRepository(),
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

    [Fact]
    public async Task Commit_ExpiredReservation_RecordsLateCommitReviewAndReturnsPendingReview()
    {
        var setup = await ReserveAndExpireAsync("late-commit-review");
        var sourceEventId = Guid.NewGuid();

        var result = await CommitAsync(setup, sourceEventId, "late-payment-event-1");

        Assert.True(result.IsFailure);
        Assert.Equal("Codes.CommitReservation.LateCommitPendingReview", result.Error.Code);
        var review = Assert.Single(setup.Reviews.Reviews);
        Assert.Equal(setup.Reservation.Id, review.CodeReservationId);
        Assert.Equal(sourceEventId, review.SourceEventId);
        Assert.Equal(setup.Reservation.Payment.PaymentId, review.RelatedPaymentId);
        Assert.Equal("Codes.PaymentOutcomeVerifier.NotImplemented", review.ErrorCode);
        Assert.Empty(setup.Redemptions.Redemptions);
    }

    [Fact]
    public async Task Commit_ExpiredReservationReplayed_DoesNotDuplicateReview()
    {
        var setup = await ReserveAndExpireAsync("late-commit-replay");
        var sourceEventId = Guid.NewGuid();

        var first = await CommitAsync(setup, sourceEventId, "late-payment-event-1");
        var second = await CommitAsync(setup, sourceEventId, "late-payment-event-1");

        Assert.Equal("Codes.CommitReservation.LateCommitPendingReview", first.Error.Code);
        Assert.Equal("Codes.CommitReservation.LateCommitPendingReview", second.Error.Code);
        Assert.Single(setup.Reviews.Reviews);
    }

    [Fact]
    public async Task Commit_ExpiredReservation_PaymentNotConfirmed_GoesToReview()
    {
        var setup = await ReserveAndExpireAsync("subject-not-confirmed");

        var result = await CommitAsync(
            setup,
            Guid.NewGuid(),
            "late-not-confirmed",
            new FixedVerifier(PaymentOutcomeVerifierErrors.NotConfirmed)
        );

        Assert.Equal(CommitReservationHandler.LateCommitPendingReviewCode, result.Error.Code);
        Assert.Equal(PaymentOutcomeVerifierErrors.NotConfirmed, Assert.Single(setup.Reviews.Reviews).ErrorCode);
    }

    [Fact]
    public async Task Commit_ExpiredReservation_VerifierUnavailable_IsRetriedNotReviewed()
    {
        var setup = await ReserveAndExpireAsync("subject-unavailable");

        var result = await CommitAsync(
            setup,
            Guid.NewGuid(),
            "late-unavailable",
            new FixedVerifier(PaymentOutcomeVerifierErrors.Unavailable)
        );

        Assert.Equal(PaymentOutcomeVerifierErrors.Unavailable, result.Error.Code);
        Assert.Empty(setup.Reviews.Reviews);
    }

    private sealed class FixedVerifier(string code) : IPaymentOutcomeVerifier
    {
        public Task<Result> VerifySucceededAsync(PaymentOutcomeQuery query, CancellationToken ct = default) =>
            Task.FromResult(Result.Failure(new Error(code, "verifier says no")));
    }

    private sealed record LateCommitSetup(
        InMemoryCodeDefinitionRepository Definitions,
        InMemoryCodeReservationRepository Reservations,
        InMemoryCodeRedemptionRepository Redemptions,
        InMemoryCodeUsageCounterRepository Counters,
        InMemoryLateCommitReviewRepository Reviews,
        FakeBusinessIdempotencyExecutor Idempotency,
        FixedTimeProvider Clock,
        TaxVision.Codes.Domain.Reservations.CodeReservation Reservation
    );

    private static async Task<LateCommitSetup> ReserveAndExpireAsync(string subjectId)
    {
        var definition = GrowthTestData.CreateActivePercentageCode();
        var quote = GrowthTestData.CreateQuote(definition, GrowthTestData.RefereeTenantId, subjectId: subjectId);
        var definitions = new InMemoryCodeDefinitionRepository(definition);
        var reservations = new InMemoryCodeReservationRepository();
        var counters = new InMemoryCodeUsageCounterRepository();
        var idempotency = new FakeBusinessIdempotencyExecutor();
        var clock = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));

        var reserved = await ReserveCodeHandler.Handle(
            new ReserveCodeCommand(
                GrowthTestData.RefereeTenantId,
                quote.Id,
                "PaymentApp",
                Guid.NewGuid(),
                $"reserve-{subjectId}",
                300
            ),
            definitions,
            new InMemoryCodeQuoteRepository(quote),
            reservations,
            counters,
            idempotency,
            clock,
            CancellationToken.None
        );
        Assert.True(reserved.IsSuccess);

        var reservation = Assert.Single(reservations.Reservations);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(reservation.Expire(clock.GetUtcNow().UtcDateTime).IsSuccess);

        return new LateCommitSetup(
            definitions,
            reservations,
            new InMemoryCodeRedemptionRepository(),
            counters,
            new InMemoryLateCommitReviewRepository(),
            idempotency,
            clock,
            reservation
        );
    }

    private static Task<Result<CommitReservationResponse>> CommitAsync(
        LateCommitSetup setup,
        Guid sourceEventId,
        string idempotencyKey,
        IPaymentOutcomeVerifier? verifier = null
    ) =>
        CommitReservationHandler.Handle(
            new CommitReservationCommand(
                GrowthTestData.RefereeTenantId,
                setup.Reservation.Id,
                setup.Reservation.Payment.Source,
                setup.Reservation.Payment.PaymentId,
                setup.Reservation.SnapshotHash.Value,
                sourceEventId,
                idempotencyKey
            ),
            setup.Definitions,
            setup.Reservations,
            setup.Redemptions,
            setup.Counters,
            verifier ?? new NotImplementedPaymentOutcomeVerifier(),
            setup.Reviews,
            setup.Idempotency,
            setup.Clock,
            CancellationToken.None
        );

    private sealed class NotImplementedPaymentOutcomeVerifier : IPaymentOutcomeVerifier
    {
        public Task<Result> VerifySucceededAsync(PaymentOutcomeQuery query, CancellationToken ct = default) =>
            Task.FromResult(
                Result.Failure(new Error("Codes.PaymentOutcomeVerifier.NotImplemented", "Late commit was denied."))
            );
    }

    private sealed class SuccessfulPaymentOutcomeVerifier : IPaymentOutcomeVerifier
    {
        public Task<Result> VerifySucceededAsync(PaymentOutcomeQuery query, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }
}
