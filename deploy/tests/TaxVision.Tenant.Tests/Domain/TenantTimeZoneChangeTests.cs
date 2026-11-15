using TaxVision.Tenant.Application.Tenants.DomainEvents;
using TaxVision.Tenant.Domain.Events;
using Wolverine;
using Xunit;

namespace TaxVision.Tenant.Tests.Domain;

/// <summary>
/// P9: un cambio de zona de la oficina se anuncia aparte, con la zona anterior y la nueva, para que Calendar y Tasks
/// puedan re-anclar. Un renombre o la misma zona no lo anuncian.
/// </summary>
public sealed class TenantTimeZoneChangeTests
{
    [Fact]
    public void A_new_zone_is_announced_with_the_previous_one()
    {
        var tenant = TaxVision
            .Tenant.Domain.Tenant.Create("Demo", "demo-office", "America/New_York", DateTime.UtcNow)
            .Value;
        var nowUtc = tenant.UpdatedAtUtc.AddMinutes(1);

        Assert.True(tenant.UpdateNameAndTimeZone("Demo", "America/Chicago", nowUtc).Value);

        var changed = Assert.Single(tenant.DomainEvents.OfType<TenantTimeZoneChangedDomainEvent>());
        Assert.Equal("America/New_York", changed.PreviousTimeZoneId);
        Assert.Equal("America/Chicago", changed.NewTimeZoneId);
        Assert.Equal(tenant.Version, changed.Version);
        Assert.Equal(nowUtc, changed.ChangedAtUtc);
    }

    [Fact]
    public void A_rename_or_the_same_zone_is_not_a_zone_change()
    {
        var tenant = TaxVision
            .Tenant.Domain.Tenant.Create("Demo", "demo-office", "America/New_York", DateTime.UtcNow)
            .Value;

        tenant.UpdateNameAndTimeZone("Acme", "America/New_York", DateTime.UtcNow);
        tenant.ChangeTimeZone("America/New_York", DateTime.UtcNow);

        Assert.Empty(tenant.DomainEvents.OfType<TenantTimeZoneChangedDomainEvent>());
    }

    [Fact]
    public void The_integration_event_carries_both_zones()
    {
        var tenantId = Guid.NewGuid();
        var at = new DateTime(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);

        var evt = TenantTimeZoneChangedDomainEventHandler.Handle(
            new TenantTimeZoneChangedDomainEvent(tenantId, "America/New_York", "America/Chicago", at, 3),
            new Envelope { CorrelationId = "c-1" }
        );

        Assert.Equal(tenantId, evt.TenantId);
        Assert.Equal(tenantId, evt.ChangedTenantId);
        Assert.Equal("America/New_York", evt.PreviousTimeZoneId);
        Assert.Equal("America/Chicago", evt.NewTimeZoneId);
        Assert.Equal(at, evt.ChangedAtUtc);
        Assert.Equal(3, evt.TenantVersion);
        Assert.Equal("c-1", evt.CorrelationId);
    }
}
