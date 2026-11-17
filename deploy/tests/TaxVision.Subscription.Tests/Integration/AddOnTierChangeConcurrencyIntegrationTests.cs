using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns.TierChange;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Tests.Domain;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// Doble clic en "Upgrade": las dos peticiones leen el paquete chico antes de guardar; la RowVersion de
/// TenantAddOns deja guardar solo a una, y solo esa publica el cobro de la diferencia.
/// </summary>
public sealed class AddOnTierChangeConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();
    private readonly AddOnDefinition _small = AddOnTierChangeTests.Pack("addon.storage.small", 3m, 2);
    private readonly AddOnDefinition _large = AddOnTierChangeTests.Pack("addon.storage.large", 10m, 10);
    private Guid _addOnId;

    public async Task InitializeAsync()
    {
        var subscription = await _db.SeedActiveSubscriptionAsync();
        await using var db = _db.NewContext();
        var addOn = TenantAddOn
            .Purchase(
                _db.TenantId,
                _small,
                1,
                Money.Create(3m, "USD").Value,
                BillingCycle.Monthly,
                true,
                Guid.Empty,
                subscription.CurrentPeriodStartUtc
            )
            .Value;
        addOn.CoTermTo(subscription.CurrentPeriodEndUtc, Guid.Empty, subscription.CurrentPeriodStartUtc);
        db.TenantAddOns.Add(addOn);
        await db.SaveChangesAsync();
        _addOnId = addOn.Id;
    }

    public Task DisposeAsync() => _db.CleanupAsync();

    [Fact]
    public async Task TwoUpgradesInParallel_ChangeOnceAndChargeOnce()
    {
        var gate = new AsyncGate(2);
        var busA = new CapturingMessageBus();
        var busB = new CapturingMessageBus();

        var outcomes = await Task.WhenAll(UpgradeAsync(gate, busA), UpgradeAsync(gate, busB));

        Assert.Single(outcomes, o => o.Error is null && o.Result!.IsSuccess);
        var loser = Assert.Single(outcomes, o => o.Error is not null);
        Assert.IsType<ConflictException>(loser.Error);
        var charges = busA.Published.Concat(busB.Published).OfType<AddOnRenewalDueIntegrationEvent>().ToList();
        Assert.Single(charges);

        await using var check = _db.NewContext();
        var stored = await check
            .TenantAddOns.IgnoreQueryFilters()
            .Include(a => a.Renewals)
            .SingleAsync(a => a.Id == _addOnId);
        Assert.Equal("addon.storage.large", stored.AddOnCode);
        Assert.Single(stored.Renewals, r => r.IdempotencyKey.StartsWith("addon-tierup-", StringComparison.Ordinal));
    }

    private async Task<Outcome> UpgradeAsync(AsyncGate gate, CapturingMessageBus bus)
    {
        await Task.Yield();
        await using var db = _db.NewContext();
        try
        {
            var addOns = new GatedTenantAddOnRepo(new TenantAddOnRepository(db), gate);
            var result = await ApplyAddOnTierChangeHandler.Handle(
                new ApplyAddOnTierChangeCommand(_db.TenantId, _addOnId, "addon.storage.large", Guid.NewGuid()),
                new AddOnTierChangeLoader(
                    addOns,
                    new TenantSubscriptionRepository(db),
                    new FakeAddOnDefinitionRepository(_small, _large)
                ),
                db,
                bus,
                new FakeCorrelationContext(),
                new FakeSubscriptionAuditLogWriter(),
                new TaxVision.Subscription.Tests.TestDoubles.NoSubscriptionDiscounts(),
                NullLogger<TenantAddOn>.Instance,
                CancellationToken.None
            );
            return new Outcome(result, null);
        }
        catch (Exception ex)
        {
            return new Outcome(null, ex);
        }
    }

    private sealed record Outcome(Result<AddOnTierChangeResponse>? Result, Exception? Error);

    /// <summary>Suelta a los dos cuando ambos leyeron el paquete chico.</summary>
    private sealed class AsyncGate(int participants)
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) == participants)
                _open.TrySetResult();
            return _open.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    private sealed class GatedTenantAddOnRepo(ITenantAddOnRepository inner, AsyncGate gate) : ITenantAddOnRepository
    {
        public async Task<TenantAddOn?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        {
            var addOn = await inner.GetByIdAsync(id, tenantId, ct);
            await gate.ArriveAsync();
            return addOn;
        }

        public Task<IReadOnlyList<TenantAddOn>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            inner.GetByTenantIdAsync(tenantId, ct);

        public Task<IReadOnlyList<TenantAddOn>> GetByTenantIdForUpdateAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => inner.GetByTenantIdForUpdateAsync(tenantId, ct);

        public Task AddAsync(TenantAddOn addOn, CancellationToken ct = default) => inner.AddAsync(addOn, ct);

        public Task<IReadOnlyList<TenantAddOn>> GetDueForRenewalAsync(
            DateTime nowUtc,
            int batch,
            CancellationToken ct = default
        ) => inner.GetDueForRenewalAsync(nowUtc, batch, ct);

        public Task<IReadOnlyList<TenantAddOn>> GetSuspendedBeforeAsync(
            DateTime cutoff,
            int batch,
            CancellationToken ct = default
        ) => inner.GetSuspendedBeforeAsync(cutoff, batch, ct);

        public Task<IReadOnlyList<TenantAddOn>> GetCancelledPastPeriodEndAsync(
            DateTime nowUtc,
            int batch,
            CancellationToken ct = default
        ) => inner.GetCancelledPastPeriodEndAsync(nowUtc, batch, ct);
    }
}
