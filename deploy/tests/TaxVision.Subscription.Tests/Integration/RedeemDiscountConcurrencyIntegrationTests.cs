using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.Discounts;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// Doble clic en "Apply": los dos canjes pasan el chequeo de descuento vigente antes de guardar; la RowVersion de
/// TenantSubscriptions deja guardar solo a uno y el otro sale con conflicto (409).
/// </summary>
public sealed class RedeemDiscountConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();

    public Task InitializeAsync() => _db.SeedActiveSubscriptionAsync(publishedPlan: true);

    public Task DisposeAsync() => _db.CleanupAsync();

    [Fact]
    public async Task Two_redeems_at_once_leave_one_discount()
    {
        var gate = new AsyncGate(2);

        var outcomes = await Task.WhenAll(RedeemAsync(gate), RedeemAsync(gate));

        var winner = Assert.Single(outcomes, o => o.Error is null);
        Assert.True(winner.Result!.IsSuccess, winner.Result.IsFailure ? winner.Result.Error.Code : null);
        Assert.IsType<ConflictException>(Assert.Single(outcomes, o => o.Error is not null).Error);
        await using var check = _db.NewContext();
        Assert.Equal(
            1,
            await check.Set<SubscriptionDiscount>().IgnoreQueryFilters().CountAsync(d => d.TenantId == _db.TenantId)
        );
    }

    private async Task<Outcome> RedeemAsync(AsyncGate gate)
    {
        await Task.Yield();
        await using var db = _db.NewContext();
        try
        {
            var result = await RedeemSubscriptionDiscountCodeHandler.Handle(
                new RedeemSubscriptionDiscountCodeCommand(_db.TenantId, "SPRING25", Guid.NewGuid()),
                new TenantSubscriptionRepository(db),
                new PlanRepository(db),
                new GatedGrowth(gate),
                db,
                new FakeSubscriptionAuditLogWriter(),
                new FakeCorrelationContext(),
                CancellationToken.None
            );
            return new Outcome(result, null);
        }
        catch (Exception ex)
        {
            return new Outcome(null, ex);
        }
    }

    private sealed record Outcome(Result<SubscriptionDiscountView>? Result, Exception? Error);

    /// <summary>Suelta a los dos cuando ambos llegaron a confirmar: los dos ya pasaron el chequeo.</summary>
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

    private sealed class GatedGrowth(AsyncGate gate) : IGrowthCodeRedemptionClient
    {
        public Task<Result<GrowthCodeQuote>> QuoteAsync(GrowthCodeQuoteRequest request, CancellationToken ct) =>
            Task.FromResult(
                Result.Success(new GrowthCodeQuote(Guid.NewGuid(), Guid.NewGuid(), 2_000, null, "USD", "Forever", null))
            );

        public Task<Result<Guid>> ReserveAsync(Guid tenantId, Guid quoteId, Guid redemptionId, CancellationToken ct) =>
            Task.FromResult(Result.Success(Guid.NewGuid()));

        public async Task<Result> CommitAsync(
            Guid tenantId,
            Guid reservationId,
            Guid redemptionId,
            string snapshotHash,
            CancellationToken ct
        )
        {
            await gate.ArriveAsync();
            return Result.Success();
        }

        public Task<Result> CancelAsync(
            Guid tenantId,
            Guid reservationId,
            Guid redemptionId,
            string reason,
            CancellationToken ct
        ) => Task.FromResult(Result.Success());
    }
}
