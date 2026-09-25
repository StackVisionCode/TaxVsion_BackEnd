using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Consumers;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>Fase 1 — al desactivar u offboardear un usuario, su asiento comprado se libera
/// (antes NADIE lo hacía: quedaba consumido por un usuario inactivo).</summary>
public sealed class UserLifecycleSeatConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task Deactivated_releases_the_users_seat_and_publishes()
    {
        var userId = Guid.NewGuid();
        var seat = AssignedSeat(userId);
        var bus = new CapturingMessageBus();
        var uow = new FakeUnitOfWork();

        await UserLifecycleSeatConsumer.Handle(
            new UserDeactivatedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = userId,
                Email = "a@b.com",
                ActorType = "TenantEmployee",
            },
            new FakeSeatRepo([seat]),
            uow,
            bus,
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            NullLogger<SubscriptionSeat>.Instance,
            CancellationToken.None
        );

        Assert.Null(seat.CurrentUserId);
        Assert.Contains(bus.Published, m => m is SeatReleasedFromUserIntegrationEvent);
        Assert.Equal(1, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Offboarded_releases_the_users_seat()
    {
        var userId = Guid.NewGuid();
        var seat = AssignedSeat(userId);
        var bus = new CapturingMessageBus();

        await UserLifecycleSeatConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = userId,
                Email = "a@b.com",
                ActorType = "TenantEmployee",
                RemovedAtUtc = DateTime.UtcNow,
            },
            new FakeSeatRepo([seat]),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            NullLogger<SubscriptionSeat>.Instance,
            CancellationToken.None
        );

        Assert.Null(seat.CurrentUserId);
        Assert.Contains(bus.Published, m => m is SeatReleasedFromUserIntegrationEvent);
    }

    [Fact]
    public async Task No_seat_for_the_user_is_a_noop()
    {
        var bus = new CapturingMessageBus();
        var uow = new FakeUnitOfWork();

        await UserLifecycleSeatConsumer.Handle(
            new UserDeactivatedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = Guid.NewGuid(),
                Email = "a@b.com",
                ActorType = "TenantEmployee",
            },
            new FakeSeatRepo([]),
            uow,
            bus,
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            NullLogger<SubscriptionSeat>.Instance,
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
        Assert.Equal(0, uow.SaveChangesCallCount);
    }

    private static SubscriptionSeat AssignedSeat(Guid userId)
    {
        var seat = SubscriptionSeat
            .Purchase(
                Tenant,
                SeatType.Standard,
                SeatSourceType.Plan,
                sourceReferenceId: null,
                Money.Create(9m, "USD").Value,
                BillingCycle.Monthly,
                autoRenew: true,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        seat.AssignTo(userId, Guid.Empty, DateTime.UtcNow, reassignmentCooldownDays: 0);
        return seat;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }
}
