using BuildingBlocks.Tenancy;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>Proyección local de la oficina: renombre / cambio de zona ordenado por el sello de Tenant.</summary>
public sealed class TenantTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static Tenant NewTenant() =>
        Tenant.Register(Guid.NewGuid(), "Acme Tax", "acme-tax", TenantKind.Customer, "Etc/UTC").Value;

    [Fact]
    public void Apply_tenant_update_copies_name_and_time_zone_and_keeps_the_stamp()
    {
        var tenant = NewTenant();

        Assert.True(tenant.ApplyTenantUpdate(" Acme Tax Group ", "America/Chicago", T0));

        Assert.Equal("Acme Tax Group", tenant.Name);
        Assert.Equal("America/Chicago", tenant.DefaultTimeZoneId);
        Assert.Equal(T0, tenant.SourceUpdatedAtUtc);
    }

    [Fact]
    public void An_older_update_is_ignored()
    {
        var tenant = NewTenant();
        tenant.ApplyTenantUpdate("Newer", "America/Chicago", T0);

        Assert.False(tenant.ApplyTenantUpdate("Older", "America/Denver", T0.AddMinutes(-1)));

        Assert.Equal("Newer", tenant.Name);
        Assert.Equal("America/Chicago", tenant.DefaultTimeZoneId);
        Assert.Equal(T0, tenant.SourceUpdatedAtUtc);
    }

    [Fact]
    public void The_same_update_reapplies_idempotently()
    {
        var tenant = NewTenant();
        tenant.ApplyTenantUpdate("Acme Tax Group", "America/Chicago", T0);

        Assert.True(tenant.ApplyTenantUpdate("Acme Tax Group", "America/Chicago", T0));

        Assert.Equal("Acme Tax Group", tenant.Name);
    }

    [Theory]
    [InlineData("", "America/Chicago")]
    [InlineData("Acme", "Not/AZone")]
    public void An_invalid_update_changes_nothing(string name, string timeZoneId)
    {
        var tenant = NewTenant();

        Assert.False(tenant.ApplyTenantUpdate(name, timeZoneId, T0));

        Assert.Equal("Acme Tax", tenant.Name);
        Assert.Equal("Etc/UTC", tenant.DefaultTimeZoneId);
        Assert.Null(tenant.SourceUpdatedAtUtc);
    }

    [Fact]
    public void A_redelivered_creation_does_not_overwrite_an_applied_rename()
    {
        var tenant = NewTenant();
        tenant.ApplyTenantUpdate("Acme Tax Group", "America/Chicago", T0);

        tenant.UpdateFromCreatedEvent("Acme Tax", "acme-new", TenantKind.Customer, "Etc/UTC");

        Assert.Equal("Acme Tax Group", tenant.Name);
        Assert.Equal("America/Chicago", tenant.DefaultTimeZoneId);
        Assert.Equal("acme-new", tenant.SubDomain);
    }

    [Fact]
    public void Creation_still_sets_name_and_zone_when_no_rename_was_applied()
    {
        var tenant = NewTenant();

        tenant.UpdateFromCreatedEvent("Acme Renamed", "acme-tax", TenantKind.Customer, "America/Denver");

        Assert.Equal("Acme Renamed", tenant.Name);
        Assert.Equal("America/Denver", tenant.DefaultTimeZoneId);
    }
}
