using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>La foto de perfil de Auth deja en el directorio el idioma, la zona y el nombre del usuario.</summary>
public sealed class UserProfileDirectoryConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Stores_language_zone_and_name()
    {
        var directory = new InMemoryUserEmailDirectoryRepository();
        var entry = directory.Seed(TenantId, UserId, "ana@example.com");

        await Handle(directory, Profile("es", "America/Chicago"));

        Assert.Equal("es", entry.Language);
        Assert.Equal("America/Chicago", entry.TimeZoneId);
        Assert.Equal("Ana López", entry.DisplayName);
    }

    [Fact]
    public async Task Missing_entry_is_not_created()
    {
        var directory = new InMemoryUserEmailDirectoryRepository();

        await Handle(directory, Profile("es", null));

        Assert.Empty(directory.Entries);
    }

    [Fact]
    public async Task Registration_stores_the_profile_too()
    {
        var directory = new InMemoryUserEmailDirectoryRepository();

        await UserEmailDirectoryUpsertConsumer.Handle(
            new UserRegisteredIntegrationEvent
            {
                TenantId = TenantId,
                UserId = UserId,
                Email = "ana@example.com",
                ActorType = "TenantEmployee",
                Name = "Ana",
                LastName = "López",
                Language = "es",
                TimeZoneId = "America/Chicago",
            },
            directory,
            new NoOpUnitOfWork(),
            new NoOpCorrelation(),
            NullLogger<UserEmailDirectoryEntry>.Instance,
            default
        );

        var entry = Assert.Single(directory.Entries);
        Assert.Equal("es", entry.Language);
        Assert.Equal("Ana López", entry.DisplayName);
    }

    private static Task Handle(
        InMemoryUserEmailDirectoryRepository directory,
        UserProfileUpdatedIntegrationEvent evt
    ) =>
        UserProfileDirectoryConsumer.Handle(
            evt,
            directory,
            new NoOpUnitOfWork(),
            new NoOpCorrelation(),
            NullLogger<UserEmailDirectoryEntry>.Instance,
            default
        );

    private static UserProfileUpdatedIntegrationEvent Profile(string? language, string? zone) =>
        new()
        {
            TenantId = TenantId,
            UserId = UserId,
            Name = "Ana",
            LastName = "López",
            Language = language,
            TimeZoneId = zone,
        };

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoOpCorrelation : ICorrelationContext
    {
        public string CorrelationId => "corr";

        public IDisposable Push(string correlationId) => new Noop();

        public void Set(string correlationId) { }

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
