using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Commands;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>
/// El FINALIZE solo confirma las reservas de código en Growth. Un commit tardío que Growth deja en revisión
/// no corta el alta; cualquier otro fallo corta para que Wolverine reintente. El documento del cobro es el
/// recibo del pago en PaymentApp: Billing (facturas de la oficina a sus clientes) no recibe nada.
/// </summary>
public sealed class OnboardingFinalizerTests
{
    private const string LateCommitPendingReviewCode = "Codes.CommitReservation.LateCommitPendingReview";

    [Fact]
    public async Task Finalize_only_confirms_the_code_reservations_in_growth()
    {
        var growth = new ScriptedGrowthClient(new Dictionary<Guid, Error>());

        var result = await NewFinalizer(growth)
            .FinalizeAsync(Command(Reservation(order: 0), Reservation(order: 1)), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(2, growth.Commits.Count);
    }

    [Fact]
    public async Task Finalize_GrowthLateCommitPendingReview_StillSucceeds()
    {
        var late = Reservation(order: 0);
        var onTime = Reservation(order: 1);
        var growth = new ScriptedGrowthClient(
            new Dictionary<Guid, Error>
            {
                [late.CodeReservationId] = new(LateCommitPendingReviewCode, "Pending manual review."),
            }
        );
        var result = await NewFinalizer(growth).FinalizeAsync(Command(late, onTime), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(2, growth.Commits.Count);
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
        var result = await NewFinalizer(growth).FinalizeAsync(Command(reservation), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Growth.Unreachable", result.Error.Code);
    }

    private static OnboardingFinalizer NewFinalizer(IGrowthOnboardingClient growth) =>
        new(growth, NullLogger<OnboardingFinalizer>.Instance);

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
    }
}
