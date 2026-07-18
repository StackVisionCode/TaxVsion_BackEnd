using BuildingBlocks.Results;
using TaxVision.Scribe.Application.EventMappings;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Tests.EventMappings;

public sealed class EventTemplateResolverTests
{
    private static readonly EventKey TestEventKey = EventKey.Create("auth.password_reset_requested.v1").Value;
    private static readonly Guid TenantId = Guid.NewGuid();

    private static EventTemplateMapping Mapping(
        TemplateScope scope,
        Guid? tenantId,
        string templateKey,
        string? locale
    ) =>
        EventTemplateMapping
            .CreateNew(
                scope,
                tenantId,
                TestEventKey,
                TemplateKey.Create(templateKey).Value,
                locale is null ? null : Locale.Create(locale).Value,
                priority: 0,
                createdAtUtc: DateTime.UtcNow
            )
            .Value;

    [Fact]
    public async Task ResolveAsync_prefers_tenant_plus_locale_over_everything_else()
    {
        var repo = new FakeRepository([
            Mapping(TemplateScope.Tenant, TenantId, "tenant-locale", "es-US"),
            Mapping(TemplateScope.Tenant, TenantId, "tenant-no-locale", null),
            Mapping(TemplateScope.System, null, "system-locale", "es-US"),
            Mapping(TemplateScope.System, null, "system-no-locale", null),
        ]);
        var resolver = new EventTemplateResolver(repo);

        var result = await resolver.ResolveAsync(TestEventKey, TenantId, Locale.Create("es-US").Value);

        Assert.Equal("tenant-locale", result?.Value);
    }

    [Fact]
    public async Task ResolveAsync_falls_back_to_tenant_without_locale_when_no_locale_match()
    {
        var repo = new FakeRepository([
            Mapping(TemplateScope.Tenant, TenantId, "tenant-no-locale", null),
            Mapping(TemplateScope.System, null, "system-locale", "es-US"),
            Mapping(TemplateScope.System, null, "system-no-locale", null),
        ]);
        var resolver = new EventTemplateResolver(repo);

        var result = await resolver.ResolveAsync(TestEventKey, TenantId, Locale.Create("es-US").Value);

        Assert.Equal("tenant-no-locale", result?.Value);
    }

    [Fact]
    public async Task ResolveAsync_falls_back_to_system_plus_locale_when_no_tenant_mapping_exists()
    {
        var repo = new FakeRepository([
            Mapping(TemplateScope.System, null, "system-locale", "es-US"),
            Mapping(TemplateScope.System, null, "system-no-locale", null),
        ]);
        var resolver = new EventTemplateResolver(repo);

        var result = await resolver.ResolveAsync(TestEventKey, TenantId, Locale.Create("es-US").Value);

        Assert.Equal("system-locale", result?.Value);
    }

    [Fact]
    public async Task ResolveAsync_falls_back_to_system_without_locale_as_last_resort()
    {
        var repo = new FakeRepository([Mapping(TemplateScope.System, null, "system-no-locale", null)]);
        var resolver = new EventTemplateResolver(repo);

        var result = await resolver.ResolveAsync(TestEventKey, TenantId, Locale.Create("es-US").Value);

        Assert.Equal("system-no-locale", result?.Value);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_when_no_candidates_exist()
    {
        var repo = new FakeRepository([]);
        var resolver = new EventTemplateResolver(repo);

        var result = await resolver.ResolveAsync(TestEventKey, TenantId, locale: null);

        Assert.Null(result);
    }

    private sealed class FakeRepository(List<EventTemplateMapping> candidates) : IEventTemplateMappingRepository
    {
        public Task AddAsync(EventTemplateMapping mapping, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<EventTemplateMapping>> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<EventTemplateMapping>> ListAsync(Guid? tenantId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<EventTemplateMapping>> GetEnabledForEventAsync(
            EventKey eventKey,
            Guid? tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<EventTemplateMapping>>(candidates);

        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
