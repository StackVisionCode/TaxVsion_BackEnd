using TaxVision.Tenant.Application.Tenants.DomainEvents;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.Events;
using Wolverine;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>TEN-4: los dos avisos llevan la versión de la oficina.</summary>
public sealed class TenantVersionEventsTests
{
    [Fact]
    public void StatusNotice_CarriesTheVersion()
    {
        var notice = TenantStatusChangedDomainEventHandler.Handle(
            new TenantStatusChangedDomainEvent(Guid.NewGuid(), EnumTenantStatus.TenantStatus.Suspended, false, 7),
            new Envelope()
        );

        Assert.Equal(7, notice.TenantVersion);
    }

    [Fact]
    public void UpdatedNotice_CarriesTheVersion()
    {
        var notice = TenantUpdatedDomainEventHandler.Handle(
            new TenantUpdatedDomainEvent(Guid.NewGuid(), "Acme", "Etc/UTC", DateTime.UtcNow, 7),
            new Envelope()
        );

        Assert.Equal(7, notice.TenantVersion);
    }
}
