using BuildingBlocks.Tenancy;
using TaxVision.PaymentApp.Domain.Tenants;

namespace TaxVision.PaymentApp.Tests.Domain;

/// <summary>
/// Cubre el gate de aislamiento que <c>TenantStatusGateMiddleware</c> consulta antes de
/// aceptar cualquier request (§42.4 del diseño). Fail-closed: cualquier estado que no sea
/// explícitamente "Customer activo" debe bloquear la operación.
/// </summary>
public sealed class TenantTests
{
    [Fact]
    public void Register_with_an_empty_id_fails()
    {
        var result = Tenant.Register(Guid.Empty, "Acme Tax", "acme", TenantKind.Customer, "America/New_York", DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.InvalidId", result.Error.Code);
    }

    [Fact]
    public void Register_with_an_invalid_time_zone_fails()
    {
        var result = Tenant.Register(Guid.NewGuid(), "Acme Tax", "acme", TenantKind.Customer, "Not/A/TimeZone", DateTime.UtcNow);

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
            PlatformTenant.Id, PlatformTenant.Name, PlatformTenant.SubDomain, TenantKind.Platform, "UTC", DateTime.UtcNow);

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

    private static Tenant CreateCustomerTenant() =>
        Tenant.Register(Guid.NewGuid(), "Acme Tax", "acme", TenantKind.Customer, "America/New_York", DateTime.UtcNow).Value;
}
