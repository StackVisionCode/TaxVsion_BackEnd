using TaxVision.PaymentApp.Application.SaaSPayments.Queries;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>GRW-1: lo que Growth recibe al verificar un canje tardío sale del estado del pago.</summary>
public sealed class GetCodeReservationPaymentOutcomeHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Theory]
    [InlineData(PaymentStatus.Succeeded, true)]
    [InlineData(PaymentStatus.PartiallyRefunded, true)]
    [InlineData(PaymentStatus.Refunded, false)]
    [InlineData(PaymentStatus.ChargedBack, false)]
    [InlineData(PaymentStatus.Pending, false)]
    [InlineData(PaymentStatus.Failed, false)]
    public async Task Only_a_charged_and_not_returned_payment_is_confirmed(PaymentStatus status, bool confirmed)
    {
        var reader = new FakeReader { ByReservation = [new(TenantId, status, "hash")] };

        var result = await HandleAsync(new("PaymentApp", Guid.NewGuid(), Guid.NewGuid(), null), reader);

        Assert.True(result.Value.Found);
        Assert.Equal(confirmed, result.Value.Confirmed);
        Assert.Equal("hash", result.Value.PromotionSnapshotHash);
    }

    [Fact]
    public async Task Onboarding_source_reads_the_onboarding_payment()
    {
        var onboardingId = Guid.NewGuid();
        var reader = new FakeReader { Onboarding = [new(Guid.Empty, PaymentStatus.Succeeded, null)] };

        var result = await HandleAsync(new("Onboarding", Guid.NewGuid(), Guid.NewGuid(), onboardingId), reader);

        Assert.True(result.Value.Confirmed);
        Assert.Equal(onboardingId, reader.LastOnboardingId);
    }

    [Fact]
    public async Task Missing_payment_is_not_found()
    {
        var result = await HandleAsync(new("PaymentApp", Guid.NewGuid(), Guid.NewGuid(), null), new FakeReader());

        Assert.False(result.Value.Found);
        Assert.False(result.Value.Confirmed);
    }

    [Fact]
    public async Task Unknown_source_is_rejected()
    {
        var result = await HandleAsync(new("PaymentClient", Guid.NewGuid(), Guid.NewGuid(), null), new FakeReader());

        Assert.Equal("PaymentApp.CodeReservationOutcome.UnsupportedSource", result.Error.Code);
    }

    private static Task<BuildingBlocks.Results.Result<CodeReservationPaymentOutcomeResponse>> HandleAsync(
        GetCodeReservationPaymentOutcomeQuery query,
        FakeReader reader
    ) => GetCodeReservationPaymentOutcomeHandler.Handle(query, reader, CancellationToken.None);

    private sealed class FakeReader : ICodeReservationPaymentReader
    {
        public List<CodeReservationPaymentRow> ByReservation { get; init; } = [];
        public List<CodeReservationPaymentRow> Onboarding { get; init; } = [];
        public Guid? LastOnboardingId { get; private set; }

        public Task<IReadOnlyList<CodeReservationPaymentRow>> FindByReservationAsync(
            Guid codeReservationId,
            Guid codeReservationPaymentId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<CodeReservationPaymentRow>>(ByReservation);

        public Task<IReadOnlyList<CodeReservationPaymentRow>> FindOnboardingAsync(
            Guid onboardingId,
            CancellationToken ct = default
        )
        {
            LastOnboardingId = onboardingId;
            return Task.FromResult<IReadOnlyList<CodeReservationPaymentRow>>(Onboarding);
        }
    }
}
