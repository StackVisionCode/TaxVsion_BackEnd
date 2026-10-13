using BuildingBlocks.Messaging.PlatformIntegrationEvents;
using BuildingBlocks.Tenancy;
using TaxVision.Tenant.Application.Platform.Abstractions;
using TaxVision.Tenant.Application.Platform.Commands;
using TaxVision.Tenant.Application.Platform.Queries;
using TaxVision.Tenant.Domain.Platform;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

public sealed class PlatformSettingsHandlersTests
{
    private static InitializePlatformSettingsCommand Seed() =>
        new("TaxOffix", "TaxOffix LLC", "taxoffix.io", "api.taxoffix.io", "support@taxoffix.io", "billing@taxoffix.io");

    private static UpdatePlatformSettingsCommand Update(string baseDomain = "taxoffix.io", string brand = "TaxOffix") =>
        new(
            Guid.NewGuid(),
            brand,
            "TaxOffix LLC",
            baseDomain,
            $"api.{baseDomain}",
            $"support@{baseDomain}",
            $"billing@{baseDomain}"
        );

    [Fact]
    public async Task Initialize_creates_the_row_and_publishes_the_first_version()
    {
        var repo = new InMemoryPlatformSettingsRepository();
        var bus = new RecordingMessageBus();
        var uow = new CountingUnitOfWork();

        var result = await InitializePlatformSettingsHandler.Handle(
            Seed(),
            repo,
            uow,
            bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.Value);
        Assert.NotNull(repo.Current);
        Assert.Equal(1, uow.SaveChangesCallCount);
        var evt = Assert.IsType<PlatformSettingsChangedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(1, evt.Version);
        Assert.Equal("taxoffix.io", evt.BaseDomain);
        Assert.Equal(PlatformTenant.Id, evt.TenantId);
    }

    [Fact]
    public async Task Initialize_never_overwrites_an_existing_row()
    {
        var repo = new InMemoryPlatformSettingsRepository();
        var bus = new RecordingMessageBus();
        await InitializePlatformSettingsHandler.Handle(
            Seed(),
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );
        repo.Current!.Update(
            "Changed By Api",
            "TaxOffix LLC",
            "taxoffix.io",
            "api.taxoffix.io",
            "support@taxoffix.io",
            "billing@taxoffix.io",
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        bus.Published.Clear();

        var result = await InitializePlatformSettingsHandler.Handle(
            Seed(),
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.False(result.Value);
        Assert.Equal("Changed By Api", repo.Current.BrandName);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Update_publishes_the_full_snapshot_with_the_new_version()
    {
        var repo = InMemoryPlatformSettingsRepository.WithSeed();
        var bus = new RecordingMessageBus();
        var uow = new CountingUnitOfWork();

        var result = await UpdatePlatformSettingsHandler.Handle(
            Update("taxoffix.com", "TaxOffix Pro"),
            repo,
            uow,
            bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Version);
        Assert.Equal("app.taxoffix.com", result.Value.AppHost);
        Assert.Equal(1, uow.SaveChangesCallCount);
        var evt = Assert.IsType<PlatformSettingsChangedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(2, evt.Version);
        Assert.Equal("TaxOffix Pro", evt.BrandName);
        Assert.Equal("api.taxoffix.com", evt.ApiHost);
        Assert.Equal("test-correlation-id", evt.CorrelationId);
    }

    [Fact]
    public async Task Update_with_identical_values_neither_saves_nor_publishes()
    {
        var repo = InMemoryPlatformSettingsRepository.WithSeed();
        var bus = new RecordingMessageBus();
        var uow = new CountingUnitOfWork();

        var result = await UpdatePlatformSettingsHandler.Handle(
            Update(),
            repo,
            uow,
            bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Version);
        Assert.Equal(0, uow.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Update_with_an_invalid_domain_fails_without_publishing()
    {
        var repo = InMemoryPlatformSettingsRepository.WithSeed();
        var bus = new RecordingMessageBus();

        var result = await UpdatePlatformSettingsHandler.Handle(
            Update("https://taxoffix.io"),
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PlatformSettings.BaseDomain", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Update_and_get_before_seeding_are_not_found()
    {
        var repo = new InMemoryPlatformSettingsRepository();

        var update = await UpdatePlatformSettingsHandler.Handle(
            Update(),
            repo,
            new CountingUnitOfWork(),
            new RecordingMessageBus(),
            new NoopCorrelationContext(),
            CancellationToken.None
        );
        var get = await GetPlatformSettingsHandler.Handle(new GetPlatformSettingsQuery(), repo, CancellationToken.None);

        Assert.Equal("PlatformSettings.NotFound", update.Error.Code);
        Assert.Equal("PlatformSettings.NotFound", get.Error.Code);
    }

    private sealed class InMemoryPlatformSettingsRepository : IPlatformSettingsRepository
    {
        public PlatformSettings? Current { get; private set; }

        public static InMemoryPlatformSettingsRepository WithSeed()
        {
            var repo = new InMemoryPlatformSettingsRepository();
            repo.Current = PlatformSettings
                .Create(
                    "TaxOffix",
                    "TaxOffix LLC",
                    "taxoffix.io",
                    "api.taxoffix.io",
                    "support@taxoffix.io",
                    "billing@taxoffix.io",
                    DateTime.UtcNow
                )
                .Value;
            return repo;
        }

        public Task<PlatformSettings?> GetAsync(CancellationToken ct = default) => Task.FromResult(Current);

        public Task AddAsync(PlatformSettings settings, CancellationToken ct = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }
}
