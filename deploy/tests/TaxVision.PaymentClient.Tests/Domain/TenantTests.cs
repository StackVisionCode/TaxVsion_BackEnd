using BuildingBlocks.Tenancy;
using TaxVision.PaymentClient.Domain.Tenants;

namespace TaxVision.PaymentClient.Tests.Domain;

/// <summary>
/// Cubre el gate de aislamiento que <c>TenantStatusGateMiddleware</c> consulta antes de
/// aceptar cualquier request. Fail-closed: cualquier estado que no sea explícitamente
/// "Customer activo" debe bloquear la operación.
/// </summary>
public sealed class TenantTests
{
    [Fact]
    public void Register_with_an_empty_id_fails()
    {
        var result = Tenant.Register(
            Guid.Empty,
            "Acme Tax",
            "acme",
            TenantKind.Customer,
            "America/New_York",
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.InvalidId", result.Error.Code);
    }

    [Fact]
    public void Register_with_an_invalid_time_zone_fails()
    {
        var result = Tenant.Register(
            Guid.NewGuid(),
            "Acme Tax",
            "acme",
            TenantKind.Customer,
            "Not/A/TimeZone",
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.InvalidTimeZone", result.Error.Code);
    }

    [Fact]
    public void A_newly_registered_customer_tenant_can_operate()
    {
        var tenant = CreateCustomerTenant();

        Assert.True(tenant.CanOperate());
    }

    [Fact]
    public void A_suspended_tenant_cannot_operate()
    {
        var tenant = CreateCustomerTenant();

        tenant.ApplyStatusChange("Suspended", isActive: false, DateTime.UtcNow);

        Assert.False(tenant.CanOperate());
    }

    [Fact]
    public void A_closed_tenant_cannot_operate()
    {
        var tenant = CreateCustomerTenant();

        tenant.ApplyStatusChange("Closed", isActive: false, DateTime.UtcNow);

        Assert.False(tenant.CanOperate());
    }

    [Fact]
    public void The_platform_tenant_can_never_operate_even_if_marked_active()
    {
        var result = Tenant.Register(
            PlatformTenant.Id,
            PlatformTenant.Name,
            PlatformTenant.SubDomain,
            TenantKind.Platform,
            "UTC",
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CanOperate());
    }

    [Fact]
    public void Recovering_an_active_status_restores_operability()
    {
        var tenant = CreateCustomerTenant();
        tenant.ApplyStatusChange("Suspended", isActive: false, DateTime.UtcNow);

        tenant.ApplyStatusChange("Active", isActive: true, DateTime.UtcNow);

        Assert.True(tenant.CanOperate());
    }

    [Fact]
    public void Without_a_profile_the_customer_sees_the_office_name()
    {
        var tenant = CreateCustomerTenant();

        Assert.Equal("Acme Tax", tenant.CustomerFacingName);
        Assert.Equal(0, tenant.ProfileVersion);
    }

    [Fact]
    public void A_profile_display_name_is_trimmed_and_shown_to_the_customer()
    {
        var tenant = CreateCustomerTenant();

        Assert.True(tenant.ApplyProfile(1, "  Acme Refunds  ", DateTime.UtcNow));

        Assert.Equal("Acme Refunds", tenant.DisplayName);
        Assert.Equal("Acme Refunds", tenant.CustomerFacingName);
        Assert.Equal(1, tenant.ProfileVersion);
    }

    [Fact]
    public void A_blank_display_name_falls_back_to_the_office_name()
    {
        var tenant = CreateCustomerTenant();
        tenant.ApplyProfile(1, "Acme Refunds", DateTime.UtcNow);

        Assert.True(tenant.ApplyProfile(2, "   ", DateTime.UtcNow));

        Assert.Null(tenant.DisplayName);
        Assert.Equal("Acme Tax", tenant.CustomerFacingName);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void A_profile_version_not_newer_than_the_applied_one_is_ignored(long version)
    {
        var tenant = CreateCustomerTenant();
        tenant.ApplyProfile(2, "Current", DateTime.UtcNow);

        Assert.False(tenant.ApplyProfile(version, "Stale", DateTime.UtcNow));

        Assert.Equal("Current", tenant.DisplayName);
        Assert.Equal(2, tenant.ProfileVersion);
    }

    private static Tenant CreateCustomerTenant() =>
        Tenant
            .Register(Guid.NewGuid(), "Acme Tax", "acme", TenantKind.Customer, "America/New_York", DateTime.UtcNow)
            .Value;
}
