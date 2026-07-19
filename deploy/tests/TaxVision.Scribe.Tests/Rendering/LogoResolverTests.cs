using BuildingBlocks.Messaging.ScribeIntegrationEvents;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Rendering;

public sealed class LogoResolverTests
{
    private static readonly SystemAssetRef SystemLogo = SystemAssetRef.Create(
        SystemAssetKeys.HeaderLogo,
        Guid.NewGuid(),
        "image/png",
        2048,
        DateTime.UtcNow
    );

    private static LogoResolver BuildResolver(
        FakeTenantLogoRefRepository logoRefs,
        FakeTenantLogoMissingNotificationRepository notifications,
        FakeMessageBus messageBus,
        FakeSystemAssetRefRepository? systemAssets = null
    ) =>
        new(
            logoRefs,
            notifications,
            systemAssets ?? FakeSystemAssetRefRepository.WithHeaderLogo(SystemLogo),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeUnitOfWork(),
            messageBus,
            NullLogger<LogoResolver>.Instance
        );

    [Fact]
    public async Task ResolveAsync_system_scope_returns_the_configured_system_logo()
    {
        var messageBus = new FakeMessageBus();
        var resolver = BuildResolver(
            new FakeTenantLogoRefRepository(),
            new FakeTenantLogoMissingNotificationRepository(),
            messageBus
        );

        var result = await resolver.ResolveAsync(LogoScope.System, Guid.NewGuid());

        Assert.Equal(SystemLogo.CloudStorageFileId, result.CloudStorageFileId);
        Assert.False(result.IsFallback);
        Assert.Empty(messageBus.Published);
    }

    [Fact]
    public async Task ResolveAsync_system_scope_without_a_seeded_logo_returns_empty_without_throwing()
    {
        var messageBus = new FakeMessageBus();
        var resolver = BuildResolver(
            new FakeTenantLogoRefRepository(),
            new FakeTenantLogoMissingNotificationRepository(),
            messageBus,
            new FakeSystemAssetRefRepository()
        );

        var result = await resolver.ResolveAsync(LogoScope.System, Guid.NewGuid());

        Assert.Equal(Guid.Empty, result.CloudStorageFileId);
        Assert.True(result.IsFallback);
        Assert.Empty(messageBus.Published);
    }

    [Fact]
    public async Task ResolveAsync_tenant_with_active_logo_returns_it_without_publishing()
    {
        var tenantId = Guid.NewGuid();
        var logoRefs = new FakeTenantLogoRefRepository();
        logoRefs.Seed(TenantLogoRef.Create(tenantId, Guid.NewGuid(), "image/jpeg", 512, 180, 60, DateTime.UtcNow));
        var messageBus = new FakeMessageBus();
        var resolver = BuildResolver(logoRefs, new FakeTenantLogoMissingNotificationRepository(), messageBus);

        var result = await resolver.ResolveAsync(LogoScope.Tenant, tenantId);

        Assert.False(result.IsFallback);
        Assert.NotEqual(SystemLogo.CloudStorageFileId, result.CloudStorageFileId);
        Assert.Empty(messageBus.Published);
    }

    [Fact]
    public async Task ResolveAsync_tenant_without_logo_falls_back_and_publishes_missing_event()
    {
        var tenantId = Guid.NewGuid();
        var messageBus = new FakeMessageBus();
        var resolver = BuildResolver(
            new FakeTenantLogoRefRepository(),
            new FakeTenantLogoMissingNotificationRepository(),
            messageBus
        );

        var result = await resolver.ResolveAsync(LogoScope.Tenant, tenantId);

        Assert.True(result.IsFallback);
        Assert.Equal(SystemLogo.CloudStorageFileId, result.CloudStorageFileId);
        var published = Assert.Single(messageBus.Published);
        var evt = Assert.IsType<ScribeTenantLogoMissingDetectedIntegrationEvent>(published);
        Assert.Equal(tenantId, evt.TenantId);
    }

    [Fact]
    public async Task ResolveAsync_tenant_without_logo_does_not_republish_within_the_same_day()
    {
        var tenantId = Guid.NewGuid();
        var notifications = new FakeTenantLogoMissingNotificationRepository();
        await notifications.AddAsync(TenantLogoMissingNotification.Create(tenantId, DateTime.UtcNow));
        var messageBus = new FakeMessageBus();
        var resolver = BuildResolver(new FakeTenantLogoRefRepository(), notifications, messageBus);

        var result = await resolver.ResolveAsync(LogoScope.Tenant, tenantId);

        Assert.True(result.IsFallback);
        Assert.Empty(messageBus.Published);
    }
}
