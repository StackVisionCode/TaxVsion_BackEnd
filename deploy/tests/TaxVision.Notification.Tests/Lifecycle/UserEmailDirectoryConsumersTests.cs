using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Lifecycle;

/// <summary>Los eventos de Auth pueden llegar desordenados: el directorio aplica solo el más nuevo.</summary>
public sealed class UserEmailDirectoryConsumersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Reactivated_processed_before_deactivated_keeps_user_active()
    {
        var userId = Guid.NewGuid();
        var directory = new InMemoryUserEmailDirectoryRepository();
        await Registered(directory, userId, T0);

        await UserEmailDirectoryReactivationConsumer.Handle(
            new UserReactivatedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = userId,
                Email = "user@example.test",
                ActorType = "TenantEmployee",
                OccurredOn = T0.AddMinutes(2),
            },
            directory,
            new CountingUnitOfWork(),
            new NoOpCorrelation(),
            NullLogger<UserEmailDirectoryEntry>.Instance,
            CancellationToken.None
        );
        await UserEmailDirectoryDeactivationConsumer.Handle(
            new UserDeactivatedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = userId,
                Email = "user@example.test",
                ActorType = "TenantEmployee",
                OccurredOn = T0.AddMinutes(1),
            },
            directory,
            new CountingUnitOfWork(),
            new NoOpCorrelation(),
            NullLogger<UserEmailDirectoryEntry>.Instance,
            CancellationToken.None
        );

        Assert.True(Assert.Single(directory.Entries).IsActive);
    }

    [Fact]
    public async Task Replayed_registered_after_offboarding_does_not_reactivate()
    {
        var userId = Guid.NewGuid();
        var directory = new InMemoryUserEmailDirectoryRepository();

        await UserEmailDirectoryOffboardingConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = userId,
                Email = "leaver@example.test",
                ActorType = "TenantEmployee",
                RemovedAtUtc = T0.AddMinutes(2),
                OccurredOn = T0.AddMinutes(2),
            },
            directory,
            new CountingUnitOfWork(),
            new NoOpCorrelation(),
            NullLogger<UserEmailDirectoryEntry>.Instance,
            CancellationToken.None
        );
        await Registered(directory, userId, T0.AddMinutes(1));

        Assert.False(Assert.Single(directory.Entries).IsActive);
    }

    private static Task Registered(InMemoryUserEmailDirectoryRepository directory, Guid userId, DateTime occurredOn) =>
        UserEmailDirectoryUpsertConsumer.Handle(
            new UserRegisteredIntegrationEvent
            {
                TenantId = TenantId,
                UserId = userId,
                Email = "user@example.test",
                ActorType = "TenantEmployee",
                OccurredOn = occurredOn,
            },
            directory,
            new CountingUnitOfWork(),
            new NoOpCorrelation(),
            NullLogger<UserEmailDirectoryEntry>.Instance,
            CancellationToken.None
        );
}
