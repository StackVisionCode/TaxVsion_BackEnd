using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Lifecycle;

/// <summary>El retiro deja inactivo al usuario en el directorio de correo, exista o no su fila.</summary>
public sealed class UserEmailDirectoryOffboardingConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Existing_entry_is_marked_inactive()
    {
        var userId = Guid.NewGuid();
        var directory = new InMemoryUserEmailDirectoryRepository();
        var entry = directory.Seed(TenantId, userId, "leaver@example.com");

        await Handle(directory, Offboarded(userId, "leaver@example.com"), new CountingUnitOfWork());

        Assert.False(entry.IsActive);
        Assert.False(entry.IsUsable);
    }

    [Fact]
    public async Task Missing_entry_is_created_inactive_with_the_event_email()
    {
        var userId = Guid.NewGuid();
        var directory = new InMemoryUserEmailDirectoryRepository();

        await Handle(directory, Offboarded(userId, "leaver@example.com"), new CountingUnitOfWork());

        var created = Assert.Single(directory.Entries);
        Assert.Equal(userId, created.UserId);
        Assert.Equal("leaver@example.com", created.Email);
        Assert.False(created.IsActive);
    }

    [Fact]
    public async Task Redelivery_does_not_duplicate_the_entry()
    {
        var userId = Guid.NewGuid();
        var directory = new InMemoryUserEmailDirectoryRepository();

        await Handle(directory, Offboarded(userId, "leaver@example.com"), new CountingUnitOfWork());
        await Handle(directory, Offboarded(userId, "leaver@example.com"), new CountingUnitOfWork());

        Assert.Single(directory.Entries);
    }

    [Fact]
    public async Task Missing_entry_without_email_is_a_noop()
    {
        var directory = new InMemoryUserEmailDirectoryRepository();
        var unitOfWork = new CountingUnitOfWork();

        await Handle(directory, Offboarded(Guid.NewGuid(), " "), unitOfWork);

        Assert.Empty(directory.Entries);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    private static Task Handle(
        InMemoryUserEmailDirectoryRepository directory,
        UserOffboardedIntegrationEvent evt,
        CountingUnitOfWork unitOfWork
    ) =>
        UserEmailDirectoryOffboardingConsumer.Handle(
            evt,
            directory,
            unitOfWork,
            new NoOpCorrelation(),
            CancellationToken.None
        );

    private static UserOffboardedIntegrationEvent Offboarded(Guid userId, string email) =>
        new()
        {
            TenantId = TenantId,
            UserId = userId,
            Email = email,
            ActorType = "TenantEmployee",
            RemovedAtUtc = DateTime.UtcNow,
        };
}
