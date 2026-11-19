using Microsoft.EntityFrameworkCore;
using TaxVision.Wallet.Application.Activity.Abstractions;
using TaxVision.Wallet.Domain.Pricing;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.Settings;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Infrastructure.Persistence;
using TaxVision.Wallet.Infrastructure.Persistence.Repositories;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>Contra SQL Server: filtros por tipo, cruce con reserva y recarga, resumen y relleno del tipo de origen.</summary>
public sealed class WalletActivityReaderIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _runId = Guid.NewGuid();
    private readonly int _priceVersion = Random.Shared.Next(100_000, int.MaxValue);
    private WalletTopUp _topUp = null!;
    private LedgerEntry _consume = null!;
    private LedgerEntry _credit = null!;

    public async Task InitializeAsync()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        var wallet = WalletAggregate.Create(_tenantId).Value;
        _topUp = WalletTopUp
            .CreateAutomatic(
                _tenantId,
                2_500,
                "USD",
                Guid.NewGuid(),
                AutoRechargeMethod.Create("Stripe", "pm_1", "Visa 4242").Value
            )
            .Value;
        var reservation = WalletReservation
            .Open(
                _tenantId,
                ReservationReference.Create("campaign-run", _runId).Value,
                Usd(30_000),
                3,
                _priceVersion,
                new ReservationTrace("Missing W-2", new ReservationChannelUnits(0, 3, 0, 0), null)
            )
            .Value;
        reservation.Settle(Usd(20_000), Usd(10_000), 2);
        db.Wallets.Add(wallet);
        db.WalletTopUps.Add(_topUp);
        db.Reservations.Add(reservation);
        db.PriceBookVersions.Add(
            PriceBookVersion
                .Publish(
                    _priceVersion,
                    new Dictionary<PriceChannel, long> { [PriceChannel.Sms] = 10_000 },
                    Guid.NewGuid()
                )
                .Value
        );
        db.LedgerEntries.AddRange(
            _credit = wallet.Credit(Usd(25_000_000), $"topup:{_topUp.Id:N}", _topUp.Id, "topup").Value,
            wallet.Reserve(Usd(30_000), $"wallet-reserve:campaign-run:{_runId:N}", _runId, "campaign-run").Value,
            _consume = wallet
                .Consume(Usd(20_000), $"wallet-consume:campaign-run:{_runId:N}", _runId, "campaign-run")
                .Value,
            wallet.Release(Usd(10_000), $"wallet-release:campaign-run:{_runId:N}", _runId, "campaign-run").Value
        );
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        await db.LedgerEntries.IgnoreQueryFilters().Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.Reservations.IgnoreQueryFilters().Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.WalletTopUps.IgnoreQueryFilters().Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.Wallets.IgnoreQueryFilters().Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Theory]
    [InlineData(TransactionKind.All, 4)]
    [InlineData(TransactionKind.TopUps, 1)]
    [InlineData(TransactionKind.Charges, 1)]
    [InlineData(TransactionKind.Reserved, 2)]
    public async Task ListAsync_FiltersByKind(TransactionKind kind, int expected)
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);

        var page = await new WalletActivityReader(db).ListAsync(
            _tenantId,
            new TransactionFilter(kind, null, null),
            1,
            20
        );

        Assert.Equal(expected, page.TotalCount);
    }

    [Fact]
    public async Task ListAsync_EntryExactlyAtTheEndIsLeftOut()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        var reader = new WalletActivityReader(db);
        var at = _credit.CreatedAtUtc;

        var endingAt = await reader.ListAsync(
            _tenantId,
            new TransactionFilter(TransactionKind.TopUps, null, at),
            1,
            20
        );
        var startingAt = await reader.ListAsync(
            _tenantId,
            new TransactionFilter(TransactionKind.TopUps, at, null),
            1,
            20
        );

        Assert.Equal(0, endingAt.TotalCount);
        Assert.Equal(1, startingAt.TotalCount);
    }

    [Fact]
    public async Task ListAsync_JoinsTheReservationAndTheTopUpOrder()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);

        var page = await new WalletActivityReader(db).ListAsync(
            _tenantId,
            new TransactionFilter(TransactionKind.All, null, null),
            1,
            20
        );

        var credit = Assert.Single(page.Items, r => r.Entry.Movement == MovementType.TopUp);
        Assert.Equal(_topUp.Id, credit.TopUp?.Id);
        Assert.Null(credit.Reservation);
        Assert.All(
            page.Items.Where(r => r.Entry.Movement != MovementType.TopUp),
            r => Assert.Equal("Missing W-2", r.Reservation?.Description)
        );
        Assert.All(page.Items.Where(r => r.Entry.Movement != MovementType.TopUp), r => Assert.Null(r.TopUp));
    }

    [Fact]
    public async Task DetailAndSummaryQueries_ReadStepsSpendAndPrices()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        var reader = new WalletActivityReader(db);

        var row = await reader.GetAsync(_tenantId, _consume.Id);
        var other = await reader.GetAsync(Guid.NewGuid(), _consume.Id);
        var steps = await reader.ListByReferenceAsync(_tenantId, "campaign-run", _runId);
        var spend = await reader.ListSpendAsync(_tenantId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        var prices = await reader.GetUnitPricesAsync([_priceVersion]);

        Assert.Equal(2, row?.Reservation?.ConsumedUnits);
        Assert.Null(other);
        Assert.Equal(3, steps.Count);
        var consumed = Assert.Single(spend);
        Assert.Equal(
            (20_000L, _priceVersion, 3),
            (consumed.SpentMicros, consumed.PriceBookVersion!.Value, consumed.Units!.Sms)
        );
        Assert.Equal(10_000, prices[_priceVersion][PriceChannel.Sms]);
    }

    [Fact]
    public async Task Backfill_FillsTheReferenceTypeOfOldEntriesFromTheirOperationKey()
    {
        var tenantId = Guid.NewGuid();
        await using (var seed = WalletTestDatabase.NewContext(tenantId))
        {
            var wallet = WalletAggregate.Create(tenantId).Value;
            seed.Wallets.Add(wallet);
            seed.LedgerEntries.AddRange(
                wallet.Credit(Usd(1_000_000), $"topup:{Guid.NewGuid():N}").Value,
                wallet.Reserve(Usd(10_000), $"wallet-reserve:individual-sms:{Guid.NewGuid():N}").Value,
                wallet.CreditReward(Usd(5_000), $"wallet-credit:referral-reward:{Guid.NewGuid():N}").Value,
                wallet.Adjust(Usd(1_000), "manual-fix").Value
            );
            await seed.SaveChangesAsync();
        }

        await using var db = WalletTestDatabase.NewContext(tenantId);
        await db.Database.ExecuteSqlRawAsync(LedgerReferenceTypeBackfill.Sql);
        var types = await db
            .LedgerEntries.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.OperationKey)
            .Select(e => new
            {
                e.OperationKey,
                e.ReferenceType,
                e.DeltaPostedMicros,
            })
            .ToListAsync();

        Assert.Equal(
            ["manual-fix", "topup", "wallet-credit", "wallet-reserve"],
            types.Select(t => t.OperationKey.Split(':')[0]).ToArray()
        );
        Assert.Equal(
            ["(none)", "topup", "referral-reward", "individual-sms"],
            types.Select(t => t.ReferenceType ?? "(none)").ToArray()
        );
        Assert.Equal([1_000L, 1_000_000L, 5_000L, 0L], types.Select(t => t.DeltaPostedMicros).ToArray());

        await db.LedgerEntries.IgnoreQueryFilters().Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Wallets.IgnoreQueryFilters().Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task SettingsRepository_SavesTheSettingsAndTheirHistory()
    {
        var tenantId = Guid.NewGuid();
        await using (var db = WalletTestDatabase.NewContext(tenantId))
        {
            var repository = new WalletSettingsRepository(db);
            var settings = WalletSettings.CreateDefault(tenantId);
            var change = settings.EnableAutoRecharge(
                2_500,
                5_000_000,
                AutoRechargeMethod.Create("Stripe", "pm_1", "Visa 4242").Value,
                new ConsentGrant(
                    Guid.NewGuid(),
                    DateTime.UtcNow,
                    "203.0.113.9",
                    AutoRechargeConsentTemplate.Version,
                    "I authorize"
                )
            );
            settings.CheckBalance(1_000_000, DateOnly.FromDateTime(DateTime.UtcNow), pendingAutomatic: false);
            await repository.AddAsync(settings);
            await repository.AddChangeAsync(change.Value);
            await db.SaveChangesAsync();
        }

        await using var read = WalletTestDatabase.NewContext(tenantId);
        var readRepository = new WalletSettingsRepository(read);
        var stored = await readRepository.GetAsync(tenantId);
        var history = await readRepository.ListChangesAsync(tenantId, 1, 10);

        Assert.Equal(AutoRechargeStatus.Active, stored!.AutoRechargeStatus);
        Assert.Equal("Visa 4242", stored.AuthorizedMethod?.Label);
        Assert.Equal(1, stored.RechargesOn(DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.True(stored.LowBalanceAlertSent);
        Assert.Equal("203.0.113.9", Assert.Single(history).IpAddress);

        await read.WalletSettingsChanges.IgnoreQueryFilters().Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
        await read.WalletSettings.IgnoreQueryFilters().Where(c => c.TenantId == tenantId).ExecuteDeleteAsync();
    }
}
