using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Reservations.CommitReservation;
using TaxVision.Codes.Application.Reservations.LateCommitReviews;
using TaxVision.Codes.Application.Reservations.ReserveCode;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Api.LateCommitReviews;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Application;

/// <summary>GRW-1: PlatformAdmin da por bueno un canje tardío solo si el dueño del pago lo confirma; queda auditado.</summary>
public sealed class ForceCommitLateReservationTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task Confirmed_payment_commits_with_the_reservation_tenant_and_resolves_the_review()
    {
        var setup = await LateCommitInReviewAsync();
        setup.Verifier.Outcome = Result.Success();

        var result = await ForceCommitAsync(setup, "paid in Stripe");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var redemption = Assert.Single(setup.Redemptions.Redemptions);
        Assert.Equal(redemption.Id, result.Value.RedemptionId);
        Assert.Equal(CodeReservationStatus.Committed, setup.Reservation.Status);
        // El commit corre con el tenant de la reserva, no con el de la plataforma.
        var commit = Assert.Single(setup.Bus.Invoked, i => i.Message is CommitReservationCommand);
        Assert.Equal(GrowthTestData.RefereeTenantId.ToString(), commit.TenantId);
        Assert.Equal(PlatformTenant.Id.ToString(), setup.Bus.TenantId);

        var review = Assert.Single(setup.Reviews.Reviews);
        Assert.Equal(LateCommitReview.ResolutionCommitted, review.Resolution);
        Assert.Equal(Actor, review.ResolvedByUserId);
        Assert.Equal("paid in Stripe", review.ResolutionNote);
        var audit = Assert.Single(setup.Audit.Records);
        Assert.Equal("LateCommitForceCommitted", audit.Action);
        Assert.Equal(Actor, audit.ActorUserId);
    }

    [Fact]
    public async Task Unconfirmed_payment_keeps_the_review_pending_and_commits_nothing()
    {
        var setup = await LateCommitInReviewAsync();
        setup.Verifier.Outcome = Result.Failure(
            new Error(PaymentOutcomeVerifierErrors.NotConfirmed, "PaymentApp does not confirm it.")
        );

        var result = await ForceCommitAsync(setup, null);

        Assert.Equal(ForceCommitLateReservationHandler.PaymentNotConfirmedCode, result.Error.Code);
        Assert.Empty(setup.Redemptions.Redemptions);
        Assert.Null(Assert.Single(setup.Reviews.Reviews).ResolvedAtUtc);
        Assert.Empty(setup.Audit.Records);
    }

    [Fact]
    public async Task Resolved_review_is_not_committed_again()
    {
        var setup = await LateCommitInReviewAsync();
        setup.Verifier.Outcome = Result.Success();
        Assert.True((await ForceCommitAsync(setup, null)).IsSuccess);

        var again = await ForceCommitAsync(setup, null);

        Assert.Equal("Codes.LateCommitReview.AlreadyResolved", again.Error.Code);
        Assert.Single(setup.Redemptions.Redemptions);
    }

    [Fact]
    public void Review_is_resolved_once_and_needs_an_actor()
    {
        var review = LateCommitReview
            .Record(
                Guid.NewGuid(),
                Guid.NewGuid(),
                PaymentReference.Create("Onboarding", Guid.NewGuid()).Value,
                Guid.NewGuid(),
                PaymentOutcomeVerifierErrors.Disabled,
                DateTime.UtcNow
            )
            .Value;

        Assert.Equal(
            "Codes.LateCommitReview.ActorRequired",
            review.Resolve(LateCommitReview.ResolutionCommitted, Guid.Empty, null, DateTime.UtcNow).Error.Code
        );
        Assert.True(review.Resolve(LateCommitReview.ResolutionCommitted, Actor, null, DateTime.UtcNow).IsSuccess);
        Assert.Equal(
            "Codes.LateCommitReview.AlreadyResolved",
            review.Resolve(LateCommitReview.ResolutionCommitted, Actor, null, DateTime.UtcNow).Error.Code
        );
    }

    private static Task<Result<ForceCommitLateReservationResponse>> ForceCommitAsync(Setup setup, string? note) =>
        ForceCommitLateReservationHandler.Handle(
            new ForceCommitLateReservationCommand(setup.ReviewId, Actor, note),
            setup.Bus,
            setup.Reviews,
            CancellationToken.None
        );

    private sealed record Setup(
        RoutingMessageBus Bus,
        InMemoryLateCommitReviewRepository Reviews,
        InMemoryCodeRedemptionRepository Redemptions,
        ConfigurableVerifier Verifier,
        RecordingAuditTrail Audit,
        CodeReservation Reservation,
        Guid ReviewId
    );

    // Reserva vencida cuyo commit tardío quedó en revisión (verificador apagado en su momento).
    private static async Task<Setup> LateCommitInReviewAsync()
    {
        var definition = GrowthTestData.CreateActivePercentageCode();
        var quote = GrowthTestData.CreateQuote(definition, GrowthTestData.RefereeTenantId, subjectId: "subject-force");
        var definitions = new InMemoryCodeDefinitionRepository(definition);
        var reservations = new InMemoryCodeReservationRepository();
        var counters = new InMemoryCodeUsageCounterRepository();
        var redemptions = new InMemoryCodeRedemptionRepository();
        var idempotency = new FakeBusinessIdempotencyExecutor();
        var reviews = new InMemoryLateCommitReviewRepository();
        var clock = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));
        var verifier = new ConfigurableVerifier();
        var audit = new RecordingAuditTrail();

        var reserved = await ReserveCodeHandler.Handle(
            new ReserveCodeCommand(
                GrowthTestData.RefereeTenantId,
                quote.Id,
                "PaymentApp",
                Guid.NewGuid(),
                "reserve-force",
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

        var review = LateCommitReview
            .Record(
                GrowthTestData.RefereeTenantId,
                reservation.Id,
                reservation.Payment,
                Guid.NewGuid(),
                PaymentOutcomeVerifierErrors.Disabled,
                clock.GetUtcNow().UtcDateTime
            )
            .Value;
        await reviews.RecordAsync(review);
        reviews.SnapshotByReservation[reservation.Id] = reservation.SnapshotHash.Value;

        var bus = new RoutingMessageBus(
            (message, _) =>
                message switch
                {
                    CommitReservationCommand commit => CommitReservationHandler.Handle(
                        commit,
                        definitions,
                        reservations,
                        redemptions,
                        counters,
                        verifier,
                        reviews,
                        idempotency,
                        clock,
                        CancellationToken.None
                    ),
                    ResolveLateCommitReviewCommand resolve => ResolveLateCommitReviewHandler.Handle(
                        resolve,
                        reviews,
                        audit,
                        new NoopUnitOfWork(),
                        clock,
                        CancellationToken.None
                    ),
                    _ => throw new NotSupportedException(message.GetType().Name),
                }
        )
        {
            TenantId = PlatformTenant.Id.ToString(),
        };

        return new Setup(bus, reviews, redemptions, verifier, audit, reservation, review.Id);
    }

    private sealed class ConfigurableVerifier : IPaymentOutcomeVerifier
    {
        public Result Outcome { get; set; } = Result.Success();

        public Task<Result> VerifySucceededAsync(PaymentOutcomeQuery query, CancellationToken ct = default) =>
            Task.FromResult(Outcome);
    }

    private sealed class RecordingAuditTrail : ICodesAuditTrail
    {
        public List<CodesAuditRecord> Records { get; } = [];

        public void Append(CodesAuditRecord record) => Records.Add(record);
    }

    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }
}
