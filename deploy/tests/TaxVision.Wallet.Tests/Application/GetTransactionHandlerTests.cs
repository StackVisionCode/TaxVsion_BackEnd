using TaxVision.Wallet.Application.Activity;
using TaxVision.Wallet.Application.Activity.Abstractions;
using TaxVision.Wallet.Application.Activity.Queries;
using TaxVision.Wallet.Domain.Pricing;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Tests.TestDoubles;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Application;

public sealed class GetTransactionHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeWalletActivityReader _reader = new();

    [Fact]
    public async Task Handle_ChargeOfACampaign_ShowsChannelsUnitsAndEveryStep()
    {
        var wallet = WalletAggregate.Create(_tenantId).Value;
        wallet.Credit(Usd(10_000_000), "topup:seed");
        var runId = Guid.NewGuid();
        var run = WalletReservation
            .Open(
                _tenantId,
                ReservationReference.Create("campaign-run", runId).Value,
                Usd(35_000),
                5,
                7,
                new ReservationTrace("Spring promo", new ReservationChannelUnits(2, 3, 0, 0), null)
            )
            .Value;
        var reserve = wallet.Reserve(Usd(35_000), "wallet-reserve:campaign-run:r", runId, "campaign-run").Value;
        run.Settle(Usd(28_000), Usd(7_000), consumedUnits: 4);
        var consume = wallet.Consume(Usd(28_000), "wallet-consume:campaign-run:r", runId, "campaign-run").Value;
        var release = wallet.Release(Usd(7_000), "wallet-release:campaign-run:r", runId, "campaign-run").Value;
        _reader.Rows.AddRange([new(reserve, run, null), new(consume, run, null), new(release, run, null)]);
        _reader.Prices[7] = new Dictionary<PriceChannel, long>
        {
            [PriceChannel.Email] = 5_000,
            [PriceChannel.Sms] = 10_000,
        };

        var result = await HandleAsync(consume.Id);

        var detail = result.Value;
        Assert.Equal("Spring promo", detail.Description);
        Assert.Equal("campaign-run", detail.ReferenceType);
        Assert.Equal(runId, detail.ReferenceId);
        Assert.Equal(7, detail.PriceBookVersion);
        Assert.Equal(
            [new ChannelChargeView("Email", 2, 5_000, 10_000), new ChannelChargeView("Sms", 3, 10_000, 30_000)],
            detail.Channels
        );
        Assert.Equal((5, 4), (detail.ReservedUnits, detail.ConsumedUnits));
        Assert.Equal(["Reserve", "Consume", "Release"], detail.Steps.Select(s => s.Movement).ToArray());
        Assert.Equal([35_000L, 28_000L, 7_000L], detail.Steps.Select(s => s.AmountMicros).ToArray());
        Assert.Null(detail.TopUp);
        Assert.Equal(consume.Id, detail.Entry.Id);
    }

    [Fact]
    public async Task Handle_TopUp_ShowsHowItWasPaid()
    {
        var wallet = WalletAggregate.Create(_tenantId).Value;
        var topUp = WalletTopUp.Create(_tenantId, 5_000, "USD", Guid.NewGuid(), "Stripe", "Card").Value;
        var credit = wallet.Credit(Usd(50_000_000), $"topup:{topUp.Id:N}", topUp.Id, "topup").Value;
        _reader.Rows.Add(new(credit, null, topUp));

        var detail = (await HandleAsync(credit.Id)).Value;

        Assert.Equal(new TopUpDetailView("Stripe", "Card", false, "Pending"), detail.TopUp);
        Assert.Equal("Top-up · Card", detail.Description);
        Assert.Empty(detail.Channels);
        Assert.Null(detail.ConsumedUnits);
    }

    [Fact]
    public async Task Handle_EntryOfAnotherTenant_IsNotFound()
    {
        var other = WalletAggregate.Create(Guid.NewGuid()).Value;
        var credit = other.Credit(Usd(1_000), "topup:x").Value;
        _reader.Rows.Add(new(credit, null, null));

        var result = await HandleAsync(credit.Id);

        Assert.Equal(WalletActivityErrors.TransactionNotFound, result.Error);
    }

    private Task<BuildingBlocks.Results.Result<TransactionDetailView>> HandleAsync(Guid entryId) =>
        GetTransactionHandler.Handle(new GetTransactionQuery(_tenantId, entryId), _reader, CancellationToken.None);
}
