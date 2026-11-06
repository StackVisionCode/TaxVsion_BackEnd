using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.Events;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class TenantDomainTests
{
    [Fact]
    public void Creation_normalizes_subdomain_and_timezone()
    {
        var result = TaxVision.Tenant.Domain.Tenant.Create(" Demo Tax Office ", "Demo-Office", "America/New_York");

        Assert.True(result.IsSuccess);
        Assert.Equal("Demo Tax Office", result.Value.Name);
        Assert.Equal("demo-office", result.Value.SubDomain);
        Assert.Equal(EnumTenantStatus.TenantStatus.Active, result.Value.Status);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("UPPER_CASE")]
    [InlineData("spaces are invalid")]
    public void Creation_rejects_invalid_subdomains(string subdomain)
    {
        var result = TaxVision.Tenant.Domain.Tenant.Create("Demo", subdomain, "America/New_York");

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.Subdomain", result.Error.Code);
    }

    [Fact]
    public void Only_active_tenant_can_be_suspended()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "America/New_York").Value;

        Assert.True(tenant.Suspend().IsSuccess);
        Assert.True(tenant.Suspend().IsFailure);
    }

    [Fact]
    public void Creation_stamps_updated_at_with_the_creation_time()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "America/New_York").Value;

        Assert.Equal(tenant.CreatedAtUtc, tenant.UpdatedAtUtc);
    }

    [Fact]
    public void Rename_trims_the_name_and_moves_the_stamp_forward()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "America/New_York").Value;
        var nowUtc = tenant.UpdatedAtUtc.AddMinutes(5);

        var result = tenant.Rename("  Acme Tax  ", nowUtc);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal("Acme Tax", tenant.Name);
        Assert.Equal(nowUtc, tenant.UpdatedAtUtc);
    }

    [Fact]
    public void Rename_to_the_same_name_reports_no_change_and_keeps_the_stamp()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "America/New_York").Value;
        var before = tenant.UpdatedAtUtc;

        var result = tenant.Rename("Demo", before.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Equal(before, tenant.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_rejects_a_blank_name(string name)
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "America/New_York").Value;

        var result = tenant.Rename(name, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.Name", result.Error.Code);
        Assert.Equal("Demo", tenant.Name);
    }

    [Fact]
    public void Rename_rejects_a_name_longer_than_the_column()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "America/New_York").Value;

        var result = tenant.Rename(new string('a', TaxVision.Tenant.Domain.Tenant.NameMaxLength + 1), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.Name", result.Error.Code);
    }

    [Fact]
    public void Change_time_zone_accepts_a_valid_iana_id()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "Etc/UTC").Value;
        var nowUtc = tenant.UpdatedAtUtc.AddMinutes(1);

        var result = tenant.ChangeTimeZone(" America/Chicago ", nowUtc);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal("America/Chicago", tenant.DefaultTimeZoneId);
        Assert.Equal(nowUtc, tenant.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public void Change_time_zone_rejects_an_invalid_id(string timeZoneId)
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "Etc/UTC").Value;

        var result = tenant.ChangeTimeZone(timeZoneId, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.DefaultTimeZoneId", result.Error.Code);
        Assert.Equal("Etc/UTC", tenant.DefaultTimeZoneId);
    }

    [Fact]
    public void The_stamp_never_moves_backwards()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "Etc/UTC").Value;
        var later = tenant.UpdatedAtUtc.AddMinutes(10);
        tenant.Rename("Acme", later);

        tenant.ChangeTimeZone("America/Chicago", later.AddMinutes(-5));

        Assert.Equal(later, tenant.UpdatedAtUtc);
    }

    [Fact]
    public void Update_name_and_time_zone_applies_nothing_when_one_value_is_invalid()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "Etc/UTC").Value;
        var before = tenant.UpdatedAtUtc;

        var result = tenant.UpdateNameAndTimeZone("Acme", "Not/AZone", before.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("Demo", tenant.Name);
        Assert.Equal(before, tenant.UpdatedAtUtc);
    }

    [Fact]
    public void Update_name_and_time_zone_reports_whether_anything_changed()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "Etc/UTC").Value;
        var nowUtc = tenant.UpdatedAtUtc.AddMinutes(1);

        Assert.True(tenant.UpdateNameAndTimeZone("Acme", "America/Chicago", nowUtc).Value);
        Assert.False(tenant.UpdateNameAndTimeZone("Acme", "America/Chicago", nowUtc.AddMinutes(1)).Value);
        Assert.Equal(nowUtc, tenant.UpdatedAtUtc);
    }

    [Fact]
    public void Suspend_RaisesTenantStatusChanged()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "Etc/UTC").Value;

        tenant.Suspend();

        var raised = Assert.IsType<TenantStatusChangedDomainEvent>(Assert.Single(tenant.DomainEvents));
        Assert.Equal(tenant.Id, raised.TenantId);
        Assert.Equal(EnumTenantStatus.TenantStatus.Suspended, raised.Status);
        Assert.False(raised.IsActive);
    }

    [Fact]
    public void CloseForOnboardingCancellation_RaisesTenantStatusChanged()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "Etc/UTC").Value;

        tenant.CloseForOnboardingCancellation();

        var raised = Assert.IsType<TenantStatusChangedDomainEvent>(Assert.Single(tenant.DomainEvents));
        Assert.Equal(EnumTenantStatus.TenantStatus.Closed, raised.Status);
        Assert.False(raised.IsActive);
    }

    [Fact]
    public void ChangeStatus_RaisesTenantStatusChanged_WithTheNewState()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "Etc/UTC").Value;
        tenant.Suspend();
        tenant.ClearDomainEvents();

        tenant.ChangeStatus(EnumTenantStatus.TenantStatus.Active);

        var raised = Assert.IsType<TenantStatusChangedDomainEvent>(Assert.Single(tenant.DomainEvents));
        Assert.Equal(EnumTenantStatus.TenantStatus.Active, raised.Status);
        Assert.True(raised.IsActive);
    }

    [Fact]
    public void A_rejected_transition_raises_nothing()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "Etc/UTC").Value;
        tenant.ChangeStatus(EnumTenantStatus.TenantStatus.Closed);
        tenant.ClearDomainEvents();

        Assert.True(tenant.ChangeStatus(EnumTenantStatus.TenantStatus.Active).IsFailure);
        Assert.True(tenant.Suspend().IsFailure);

        Assert.Empty(tenant.DomainEvents);
    }

    [Fact]
    public void Update_name_and_time_zone_raises_one_event_with_the_full_state()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "Etc/UTC").Value;
        var nowUtc = tenant.UpdatedAtUtc.AddMinutes(1);

        tenant.UpdateNameAndTimeZone("Acme", "America/Chicago", nowUtc);

        var raised = Assert.IsType<TenantUpdatedDomainEvent>(Assert.Single(tenant.DomainEvents));
        Assert.Equal("Acme", raised.Name);
        Assert.Equal("America/Chicago", raised.DefaultTimeZoneId);
        Assert.Equal(nowUtc, raised.UpdatedAtUtc);
    }

    [Fact]
    public void Rename_and_change_time_zone_raise_only_when_something_changed()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "Etc/UTC").Value;

        tenant.Rename("Demo", DateTime.UtcNow);
        tenant.ChangeTimeZone("Etc/UTC", DateTime.UtcNow);
        Assert.Empty(tenant.DomainEvents);

        tenant.Rename("Acme", DateTime.UtcNow);
        tenant.ChangeTimeZone("America/Chicago", DateTime.UtcNow);
        Assert.Equal(2, tenant.DomainEvents.OfType<TenantUpdatedDomainEvent>().Count());
    }
}
