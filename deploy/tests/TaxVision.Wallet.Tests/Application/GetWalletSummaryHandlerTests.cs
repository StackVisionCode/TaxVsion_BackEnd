using TaxVision.Wallet.Application.Activity;
using TaxVision.Wallet.Application.Activity.Abstractions;
using TaxVision.Wallet.Application.Activity.Queries;
using TaxVision.Wallet.Domain.Pricing;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Tests.TestDoubles;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Application;

public sealed class GetWalletSummaryHandlerTests
{
    // 18 nov 2026, 03:00 UTC = 17 nov 21:00 en Chicago: el "hoy" de la oficina es el 17.
    private static readonly DateTime Now = new(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeWalletActivityReader _reader = new();
    private readonly FakeWalletRepository _wallets = new();

    public GetWalletSummaryHandlerTests()
    {
        var wallet = WalletAggregate.Create(_tenantId).Value;
        wallet.Credit(Usd(10_000_000), "topup:seed");
        _wallets.Wallets.Add(wallet);
        _reader.Prices[1] = new Dictionary<PriceChannel, long> { [PriceChannel.Sms] = 10_000 };
    }

    [Fact]
    public async Task Handle_SpendIsCutByTheOfficeDayAndComparedWithThePreviousPeriod()
    {
        // 17 nov 21:00 Chicago (hoy de la oficina), SMS por reserva.
        Consume(Now, 1_000_000, sms: true);
        // 15 nov sin reserva conocida → "Other"; una devolución el 16 la descuenta.
        Consume(new DateTime(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc), 300_000, sms: false);
        _reader.Spend.Add(
            new SpendRow(
                new DateTime(2026, 11, 16, 18, 0, 0, DateTimeKind.Utc),
                MovementType.UsageRefund,
                200_000,
                null,
                null
            )
        );
        // Periodo anterior (29 oct–7 nov).
        Consume(new DateTime(2026, 11, 2, 18, 0, 0, DateTimeKind.Utc), 500_000, sms: false);

        var view = await HandleAsync("2026-11-08", "2026-11-17");

        Assert.Equal(("America/Chicago", "USD"), (view.TimeZone, view.Currency));
        Assert.Equal((new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 17)), (view.From, view.To));
        Assert.Equal(1_100_000, view.SpentMicros);
        Assert.Equal(500_000, view.PreviousSpentMicros);
        Assert.Equal(10, view.Buckets.Count);
        Assert.Equal(new SpendBucketView(new DateOnly(2026, 11, 17), 1_000_000), view.Buckets[^1]);
        Assert.Equal([new ChannelSpendView("Sms", 1_000_000), new ChannelSpendView("Other", 100_000)], view.ByChannel);
        Assert.Equal(1_600_000 / 30, view.AverageDailySpendMicros);
        Assert.Equal((int)(10_000_000 / (1_600_000 / 30)), view.EstimatedDaysLeft);
    }

    [Fact]
    public async Task Handle_RangeLongerThanAMonth_GroupsByIsoWeekStartingMonday()
    {
        var view = await HandleAsync("2026-09-01", "2026-11-17");

        Assert.Equal(new DateOnly(2026, 8, 31), view.Buckets[0].Date);
        Assert.All(view.Buckets, b => Assert.Equal(DayOfWeek.Monday, b.Date.DayOfWeek));
        Assert.Null(view.EstimatedDaysLeft);
    }

    [Fact]
    public async Task Handle_NoRange_DefaultsToTheOfficeMonthSoFar()
    {
        var view = await HandleAsync(null, null);

        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 17)), (view.From, view.To));
    }

    [Fact]
    public async Task Handle_HalfOpenInstants_FirstToEighteenthGiveEighteenBucketsAndAnEighteenDayPreviousPeriod()
    {
        // Nueva York: 1 nov 00:00 EDT = 04:00Z; 19 nov 00:00 EST = 05:00Z (fin exclusivo).
        var now = new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);
        Consume(new DateTime(2026, 10, 14, 16, 0, 0, DateTimeKind.Utc), 700_000, sms: false); // 14 oct: entra
        Consume(new DateTime(2026, 10, 13, 16, 0, 0, DateTimeKind.Utc), 900_000, sms: false); // 13 oct: no
        Consume(new DateTime(2026, 11, 19, 5, 0, 0, DateTimeKind.Utc), 400_000, sms: false); // exactamente "to": no

        var view = (
            await GetWalletSummaryHandler.Handle(
                new GetWalletSummaryQuery(_tenantId, "2026-11-01T04:00:00Z", "2026-11-19T05:00:00Z"),
                _reader,
                _wallets,
                new FakeOfficeTimeZone("America/New_York"),
                new FixedTimeProvider(now),
                CancellationToken.None
            )
        ).Value;

        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 18)), (view.From, view.To));
        Assert.Equal(18, view.Buckets.Count);
        Assert.Equal(new DateOnly(2026, 11, 18), view.Buckets[^1].Date);
        Assert.Equal(700_000, view.PreviousSpentMicros);
        Assert.Equal(0, view.SpentMicros);
    }

    [Fact]
    public async Task Handle_InvertedRange_IsRejected()
    {
        var result = await GetWalletSummaryHandler.Handle(
            new GetWalletSummaryQuery(_tenantId, "2026-11-10", "2026-11-01"),
            _reader,
            _wallets,
            new FakeOfficeTimeZone("America/Chicago"),
            new FixedTimeProvider(Now),
            CancellationToken.None
        );

        Assert.Equal(WalletActivityErrors.InvalidDateRange, result.Error);
    }

    private void Consume(DateTime atUtc, long micros, bool sms) =>
        _reader.Spend.Add(
            new SpendRow(
                atUtc,
                MovementType.Consume,
                -micros,
                sms ? 1 : null,
                sms ? new ReservationChannelUnits(0, 100, 0, 0) : null
            )
        );

    private async Task<WalletSummaryView> HandleAsync(string? from, string? to) =>
        (
            await GetWalletSummaryHandler.Handle(
                new GetWalletSummaryQuery(_tenantId, from, to),
                _reader,
                _wallets,
                new FakeOfficeTimeZone("America/Chicago"),
                new FixedTimeProvider(Now),
                CancellationToken.None
            )
        ).Value;

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
