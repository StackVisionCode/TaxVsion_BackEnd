using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Infrastructure.Providers;

namespace TaxVision.Postmaster.Tests.Providers;

public sealed class ProviderResolverTests
{
    private static SystemEmailProvider CreateSystemProvider() =>
        SystemEmailProvider
            .Create(
                providerCode: "smtp-default",
                displayName: "Default SMTP",
                providerType: EmailProviderType.Smtp,
                fromAddressDefault: "no-reply@taxvision.local",
                fromDisplayNameDefault: "TaxVision",
                host: "localhost",
                port: 1025,
                useTls: false,
                username: null,
                passwordCipher: "system-secret",
                rateLimitPerMinute: 60,
                createdAtUtc: DateTime.UtcNow
            )
            .Value;

    private static TenantEmailProvider CreateTenantProvider(Guid tenantId) =>
        TenantEmailProvider
            .Create(
                tenantId: tenantId,
                providerCode: "tenant-smtp",
                displayName: "Tenant SMTP",
                providerType: EmailProviderType.Smtp,
                fromAddressDefault: "billing@tenant.example",
                fromDisplayNameDefault: "Tenant Corp",
                host: "smtp.tenant.example",
                port: 587,
                useTls: true,
                username: "tenant-user",
                passwordCipher: "tenant-secret",
                rateLimitPerMinute: 30,
                createdByUserId: Guid.NewGuid(),
                createdAtUtc: DateTime.UtcNow
            )
            .Value;

    [Fact]
    public async Task Resolve_returns_system_provider_when_scope_is_System()
    {
        var systemRepo = new FakeSystemEmailProviderRepository();
        await systemRepo.AddAsync(CreateSystemProvider());
        var resolver = new ProviderResolver(
            systemRepo,
            new FakeTenantEmailProviderRepository(),
            new FakeProviderHealthStatusRepository(),
            new FakeSecretProtector()
        );

        var result = await resolver.ResolveAsync(Guid.NewGuid(), ProviderScope.System, null, CancellationToken.None);

        Assert.Equal(ProviderResolutionStatus.Resolved, result.Status);
        Assert.Equal("smtp-default", result.Provider!.ProviderCode);
    }

    [Fact]
    public async Task Resolve_returns_tenant_provider_when_exists_and_scope_is_Tenant()
    {
        var tenantId = Guid.NewGuid();
        var tenantRepo = new FakeTenantEmailProviderRepository();
        await tenantRepo.AddAsync(CreateTenantProvider(tenantId));
        var resolver = new ProviderResolver(
            new FakeSystemEmailProviderRepository(),
            tenantRepo,
            new FakeProviderHealthStatusRepository(),
            new FakeSecretProtector()
        );

        var result = await resolver.ResolveAsync(tenantId, ProviderScope.Tenant, null, CancellationToken.None);

        Assert.Equal(ProviderResolutionStatus.Resolved, result.Status);
        Assert.Equal("tenant-smtp", result.Provider!.ProviderCode);
    }

    [Fact]
    public async Task Resolve_returns_ProviderNotConfigured_when_scope_is_Tenant_and_no_provider()
    {
        var resolver = new ProviderResolver(
            new FakeSystemEmailProviderRepository(),
            new FakeTenantEmailProviderRepository(),
            new FakeProviderHealthStatusRepository(),
            new FakeSecretProtector()
        );

        var result = await resolver.ResolveAsync(Guid.NewGuid(), ProviderScope.Tenant, null, CancellationToken.None);

        Assert.Equal(ProviderResolutionStatus.ProviderNotConfigured, result.Status);
        Assert.Null(result.Provider);
    }

    [Fact]
    public async Task Resolve_returns_ProviderUnhealthy_when_tenant_circuit_breaker_is_open()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = CreateTenantProvider(tenantId);
        var tenantRepo = new FakeTenantEmailProviderRepository();
        await tenantRepo.AddAsync(tenantProvider);

        var healthRepo = new FakeProviderHealthStatusRepository();
        var health = ProviderHealthStatus
            .Create(ProviderKind.Tenant, tenantId, tenantProvider.ProviderCode, DateTime.UtcNow)
            .Value;
        health.RecordFailure(DateTime.UtcNow);
        health.RecordFailure(DateTime.UtcNow);
        health.RecordFailure(DateTime.UtcNow); // 3 fallos consecutivos abre el circuit breaker
        await healthRepo.AddAsync(health);

        var resolver = new ProviderResolver(
            new FakeSystemEmailProviderRepository(),
            tenantRepo,
            healthRepo,
            new FakeSecretProtector()
        );

        var result = await resolver.ResolveAsync(tenantId, ProviderScope.Tenant, null, CancellationToken.None);

        Assert.Equal(ProviderResolutionStatus.ProviderUnhealthy, result.Status);
        Assert.Null(result.Provider);
    }

    [Fact]
    public async Task Resolve_returns_SystemProviderMissing_when_no_system_provider_enabled()
    {
        var resolver = new ProviderResolver(
            new FakeSystemEmailProviderRepository(),
            new FakeTenantEmailProviderRepository(),
            new FakeProviderHealthStatusRepository(),
            new FakeSecretProtector()
        );

        var result = await resolver.ResolveAsync(Guid.NewGuid(), ProviderScope.System, null, CancellationToken.None);

        Assert.Equal(ProviderResolutionStatus.SystemProviderMissing, result.Status);
    }

    [Fact]
    public async Task Resolve_honors_ForceSystem_priority_hint_even_when_scope_is_Tenant()
    {
        var systemRepo = new FakeSystemEmailProviderRepository();
        await systemRepo.AddAsync(CreateSystemProvider());
        var resolver = new ProviderResolver(
            systemRepo,
            new FakeTenantEmailProviderRepository(),
            new FakeProviderHealthStatusRepository(),
            new FakeSecretProtector()
        );

        var result = await resolver.ResolveAsync(
            Guid.NewGuid(),
            ProviderScope.Tenant,
            ProviderPriorityHint.ForceSystem,
            CancellationToken.None
        );

        Assert.Equal(ProviderResolutionStatus.Resolved, result.Status);
        Assert.Equal("smtp-default", result.Provider!.ProviderCode);
    }
}
