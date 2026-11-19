using TaxVision.Wallet.Application.Activity;
using TaxVision.Wallet.Application.Activity.Abstractions;
using TaxVision.Wallet.Application.Wallet.Queries;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.Settings;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Tests.TestDoubles;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Application;

public sealed class ListLedgerHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeWalletActivityReader _reader = new();

    [Fact]
    public async Task Handle_EachRowSaysWhatItWasWithoutAnotherCall()
    {
        var wallet = WalletAggregate.Create(_tenantId).Value;
        var userId = Guid.NewGuid();
        var topUp = WalletTopUp
            .CreateAutomatic(
                _tenantId,
                2_500,
                "USD",
                userId,
                AutoRechargeMethod.Create("Stripe", "pm_1", "Visa 4242").Value
            )
            .Value;
        var credit = wallet.Credit(Usd(25_000_000), "topup:a", topUp.Id, "topup").Value;
        var smsId = Guid.NewGuid();
        var sms = Reservation("individual-sms", smsId, null, null);
        var reserve = wallet.Reserve(Usd(30_000), "wallet-reserve:individual-sms:a", smsId, "individual-sms").Value;
        var runId = Guid.NewGuid();
        var run = Reservation("campaign-run", runId, "Missing W-2", userId);
        wallet.Reserve(Usd(30_000), "wallet-reserve:campaign-run:b", runId, "campaign-run");
        run.Settle(Usd(20_000), Usd(10_000), consumedUnits: 2);
        var consume = wallet.Consume(Usd(20_000), "wallet-consume:campaign-run:b", runId, "campaign-run").Value;
        _reader.Rows.AddRange([new(consume, run, null), new(reserve, sms, null), new(credit, null, topUp)]);

        var page = await HandleAsync(new ListLedgerQuery(_tenantId, 1, 20));

        var items = page.Items;
        Assert.Equal(("Missing W-2", 2, userId), (items[0].Description, items[0].Units, items[0].RequestedByUserId));
        Assert.Equal("campaign-run", items[0].ReferenceType);
        Assert.Equal(("SMS", 3), (items[1].Description, items[1].Units));
        Assert.Equal("Automatic top-up · Visa 4242", items[2].Description);
        Assert.True(items[2].Automatic);
        Assert.False(items[0].Automatic);
    }

    [Fact]
    public async Task Handle_OfficeDaysAndKind_BecomeAUtcFilter()
    {
        await HandleAsync(
            new ListLedgerQuery(_tenantId, 1, 20, TransactionKind.Charges, "2026-11-01", "2026-11-30"),
            "America/Chicago"
        );

        Assert.Equal(TransactionKind.Charges, _reader.LastFilter!.Kind);
        Assert.Equal(new DateTime(2026, 11, 1, 5, 0, 0, DateTimeKind.Utc), _reader.LastFilter.FromUtc);
        Assert.Equal(new DateTime(2026, 12, 1, 6, 0, 0, DateTimeKind.Utc), _reader.LastFilter.ToUtcExclusive);
    }

    [Fact]
    public async Task Handle_InstantsAreAHalfOpenRange()
    {
        // Lo que manda el CRM: medianoche de inicio y medianoche siguiente de la oficina, en UTC.
        await HandleAsync(
            new ListLedgerQuery(_tenantId, 1, 20, TransactionKind.All, "2026-11-01T04:00:00Z", "2026-11-19T05:00:00Z")
        );

        Assert.Equal(new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc), _reader.LastFilter!.FromUtc);
        // Un asiento exactamente en "to" no entra: el reader filtra CreatedAtUtc < ToUtcExclusive.
        Assert.Equal(new DateTime(2026, 11, 19, 5, 0, 0, DateTimeKind.Utc), _reader.LastFilter.ToUtcExclusive);
    }

    [Fact]
    public async Task Handle_FromAfterTo_IsRejected()
    {
        var result = await ListLedgerHandler.Handle(
            new ListLedgerQuery(_tenantId, 1, 20, TransactionKind.All, "2026-11-10", "2026-11-01"),
            _reader,
            new FakeOfficeTimeZone(),
            CancellationToken.None
        );

        Assert.Equal(WalletActivityErrors.InvalidDateRange, result.Error);
    }

    private WalletReservation Reservation(string type, Guid id, string? description, Guid? userId)
    {
        var units = new ReservationChannelUnits(0, 3, 0, 0);
        return WalletReservation
            .Open(
                _tenantId,
                ReservationReference.Create(type, id).Value,
                Usd(30_000),
                3,
                1,
                new ReservationTrace(description, units, userId)
            )
            .Value;
    }

    private async Task<BuildingBlocks.Common.PagedResult<TaxVision.Wallet.Application.Wallet.LedgerEntryView>> HandleAsync(
        ListLedgerQuery query,
        string zone = "UTC"
    ) => (await ListLedgerHandler.Handle(query, _reader, new FakeOfficeTimeZone(zone), CancellationToken.None)).Value;
}
