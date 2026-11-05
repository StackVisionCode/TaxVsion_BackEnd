using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Seats;
using TaxVision.Subscription.Application.Seats.IntegrationEvents;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// SUB-1: el consumidor del pago y el job de reconciliación aprovisionan la misma intención a la vez.
/// Con RowVersion en la intención, el segundo guardado choca y no deja asientos de más.
/// </summary>
public sealed class SeatCheckoutProvisioningConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();
    private Guid _intentId;

    public async Task InitializeAsync()
    {
        await _db.SeedActiveSubscriptionAsync();
        _intentId = await SeedPendingIntentAsync(_db, quantity: 3);
    }

    public Task DisposeAsync() => _db.CleanupAsync();

    internal static async Task<Guid> SeedPendingIntentAsync(SubscriptionIntegrationDb database, int quantity)
    {
        await using var db = database.NewContext();
        var nowUtc = DateTime.UtcNow;
        var intent = SeatPurchaseIntent
            .Create(
                database.TenantId,
                SeatType.Standard,
                quantity,
                autoRenew: true,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                proratedTotalCents: 1500L * quantity,
                Guid.NewGuid(),
                nowUtc.AddMinutes(-2)
            )
            .Value;
        intent.AttachCheckout(Guid.NewGuid(), "https://checkout.test/s", nowUtc.AddHours(1), nowUtc.AddMinutes(-2));
        db.SeatPurchaseIntents.Add(intent);
        await db.SaveChangesAsync();
        return intent.Id;
    }

    internal static async Task ProvisionInAsync(SubscriptionDbContext db, Guid tenantId, Guid intentId)
    {
        var intent = await new SeatPurchaseIntentRepository(db).GetByIdForProvisioningAsync(intentId);
        var subscription = await new TenantSubscriptionRepository(db).GetByTenantIdAsync(tenantId);
        var nowUtc = DateTime.UtcNow;
        await SeatCheckoutProvisioning.TryProvisionAsync(
            intent!,
            subscription!,
            new SubscriptionSeatRepository(db),
            new SubscriptionAuditLogWriter(db),
            new FakeSubscriptionMetrics(),
            correlationId: "test",
            provisioningMethod: "Test",
            nowUtc,
            nowUtc,
            CancellationToken.None
        );
    }

    [Fact]
    public async Task TwoProvisionersSameIntent_CreateSeatsOnce()
    {
        await using var first = _db.NewContext();
        await using var second = _db.NewContext();
        await ProvisionInAsync(first, _db.TenantId, _intentId);
        await ProvisionInAsync(second, _db.TenantId, _intentId);

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var check = _db.NewContext();
        Assert.Equal(3, await check.Seats.IgnoreQueryFilters().CountAsync(s => s.TenantId == _db.TenantId));
        Assert.Equal(
            1,
            await check.AuditLogs.CountAsync(a =>
                a.TenantId == _db.TenantId
                && a.AggregateType == "SeatPurchaseIntent"
                && a.Action == "Seats.Purchased"
            )
        );
    }

    [Fact]
    public async Task ConsumerRetryAfterConflict_SeesProvisioned()
    {
        await using (var first = _db.NewContext())
        await using (var second = _db.NewContext())
        {
            await ProvisionInAsync(first, _db.TenantId, _intentId);
            await ProvisionInAsync(second, _db.TenantId, _intentId);
            await first.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }

        // Reintento del consumidor en un contexto nuevo: relee Provisioned y sale sin tocar nada.
        await using var retry = _db.NewContext();
        var bus = new CapturingMessageBus();
        await SeatsCheckoutPaidConsumer.Handle(
            new SeatsCheckoutPaidIntegrationEvent
            {
                TenantId = _db.TenantId,
                SeatPurchaseIntentId = _intentId,
                SaaSPaymentId = Guid.NewGuid(),
                AmountPaidCents = 4500,
                Currency = "USD",
                PaidAtUtc = DateTime.UtcNow,
                ProviderPaymentReference = "pi_test",
            },
            new SeatPurchaseIntentRepository(retry),
            new TenantSubscriptionRepository(retry),
            new SubscriptionSeatRepository(retry),
            retry,
            bus,
            new FakeCorrelationContext(),
            new SubscriptionAuditLogWriter(retry),
            new FakeSubscriptionMetrics(),
            NullLogger<SubscriptionSeat>.Instance,
            CancellationToken.None
        );

        Assert.False(retry.ChangeTracker.HasChanges());
        Assert.Empty(bus.Published.OfType<RecalculateEntitlementsCommand>());
        Assert.Equal(3, await retry.Seats.IgnoreQueryFilters().CountAsync(s => s.TenantId == _db.TenantId));
    }
}
