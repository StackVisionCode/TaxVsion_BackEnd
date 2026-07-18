using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Tests.EventMappings;

public sealed class EventTemplateMappingTests
{
    private static EventKey ValidEventKey() => EventKey.Create("auth.password_reset_requested.v1").Value;

    private static TemplateKey ValidTemplateKey(string value = "auth.password_reset") =>
        TemplateKey.Create(value).Value;

    [Fact]
    public void CreateNew_rejects_tenant_scope_without_tenant_id()
    {
        var result = EventTemplateMapping.CreateNew(
            TemplateScope.Tenant,
            tenantId: null,
            ValidEventKey(),
            ValidTemplateKey(),
            locale: null,
            priority: 0,
            createdAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EventTemplateMapping.TenantRequired", result.Error.Code);
    }

    [Fact]
    public void CreateNew_rejects_system_scope_with_tenant_id()
    {
        var result = EventTemplateMapping.CreateNew(
            TemplateScope.System,
            tenantId: Guid.NewGuid(),
            ValidEventKey(),
            ValidTemplateKey(),
            locale: null,
            priority: 0,
            createdAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EventTemplateMapping.TenantNotAllowed", result.Error.Code);
    }

    [Fact]
    public void CreateNew_succeeds_and_starts_enabled()
    {
        var result = EventTemplateMapping.CreateNew(
            TemplateScope.System,
            tenantId: null,
            ValidEventKey(),
            ValidTemplateKey(),
            locale: null,
            priority: 5,
            createdAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Enabled);
        Assert.Equal(5, result.Value.Priority);
    }

    [Fact]
    public void Rebind_updates_content_without_touching_identity()
    {
        var mapping = EventTemplateMapping
            .CreateNew(
                TemplateScope.System,
                tenantId: null,
                ValidEventKey(),
                ValidTemplateKey(),
                locale: null,
                priority: 0,
                createdAtUtc: DateTime.UtcNow
            )
            .Value;

        var newTemplateKey = ValidTemplateKey("auth.password_reset_v2");
        var result = mapping.Rebind(newTemplateKey, priority: 10, enabled: false, updatedAtUtc: DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(newTemplateKey, mapping.TemplateKey);
        Assert.Equal(10, mapping.Priority);
        Assert.False(mapping.Enabled);
        Assert.Equal(TemplateScope.System, mapping.Scope);
        Assert.NotNull(mapping.UpdatedAtUtc);
    }
}
