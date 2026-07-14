using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.TenantDomains.Events;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>
/// Fase A7 — cada mutación de TenantDomain encola exactamente el domain event que le
/// corresponde. AuthDbContext.SaveChangesAsync es quien los drena y despacha (ver
/// Infrastructure/AuthDbContextDomainEventDispatchTests); acá solo se prueba el agregado.
/// </summary>
public sealed class TenantDomainDomainEventsTests
{
    private static SubdomainSlug ValidSlug() => SubdomainSlug.Create("oficina1").Value;

    [Fact]
    public void CreateSubdomain_raises_TenantDomainCreated()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        var domain = TenantDomain
            .CreateSubdomain(tenantId, ValidSlug(), "taxprocore.com", createdBy, DateTime.UtcNow)
            .Value;

        var evt = Assert.Single(domain.DomainEvents.OfType<TenantDomainCreated>());
        Assert.Equal(tenantId, evt.TenantId);
        Assert.Equal(domain.Id, evt.DomainId);
        Assert.Equal("oficina1.taxprocore.com", evt.Host);
        Assert.Equal("Subdomain", evt.DomainType);
        Assert.Equal(createdBy, evt.ActingUserId);
    }

    [Fact]
    public void CreateSubdomain_with_system_actor_normalizes_empty_guid_to_null()
    {
        var domain = TenantDomain
            .CreateSubdomain(Guid.NewGuid(), ValidSlug(), "taxprocore.com", Guid.Empty, DateTime.UtcNow)
            .Value;

        var evt = Assert.Single(domain.DomainEvents.OfType<TenantDomainCreated>());
        Assert.Null(evt.ActingUserId);
    }

    [Fact]
    public void CreateCustomHostname_raises_TenantDomainCreated()
    {
        var tenantId = Guid.NewGuid();

        var domain = TenantDomain
            .CreateCustomHostname(tenantId, "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;

        var evt = Assert.Single(domain.DomainEvents.OfType<TenantDomainCreated>());
        Assert.Equal("archivos.suoficina.com", evt.Host);
        Assert.Equal("CustomHostname", evt.DomainType);
    }

    [Fact]
    public void MarkActive_raises_TenantDomainActivated()
    {
        var domain = TenantDomain
            .CreateCustomHostname(Guid.NewGuid(), "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        domain.MarkProvisioning("cf-1", "cname");
        domain.ClearDomainEvents(); // solo nos interesa lo que dispara MarkActive
        var actingUserId = Guid.NewGuid();

        domain.MarkActive(DateTime.UtcNow, actingUserId);

        var evt = Assert.Single(domain.DomainEvents.OfType<TenantDomainActivated>());
        Assert.Equal(domain.TenantId, evt.TenantId);
        Assert.Equal(domain.Id, evt.DomainId);
        Assert.Equal(actingUserId, evt.ActingUserId);
    }

    [Fact]
    public void MarkActive_failure_does_not_raise_any_event()
    {
        var domain = TenantDomain
            .CreateCustomHostname(Guid.NewGuid(), "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        domain.Disable();
        domain.ClearDomainEvents();

        var result = domain.MarkActive(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Empty(domain.DomainEvents);
    }

    [Fact]
    public void MarkFailed_raises_TenantDomainProvisioningFailed_with_reason()
    {
        var domain = TenantDomain
            .CreateCustomHostname(Guid.NewGuid(), "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        domain.MarkProvisioning("cf-1", "cname");
        domain.ClearDomainEvents();

        domain.MarkFailed("cloudflare_blocked");

        var evt = Assert.Single(domain.DomainEvents.OfType<TenantDomainProvisioningFailed>());
        Assert.Equal("cloudflare_blocked", evt.Reason);
        Assert.Null(evt.ActingUserId);
    }

    [Fact]
    public void Disable_raises_TenantDomainDisabled()
    {
        var domain = TenantDomain
            .CreateCustomHostname(Guid.NewGuid(), "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        domain.ClearDomainEvents();
        var actingUserId = Guid.NewGuid();

        domain.Disable(actingUserId);

        var evt = Assert.Single(domain.DomainEvents.OfType<TenantDomainDisabled>());
        Assert.Equal(actingUserId, evt.ActingUserId);
    }

    [Fact]
    public void Disable_of_primary_domain_does_not_raise_any_event()
    {
        var domain = TenantDomain
            .CreateSubdomain(
                Guid.NewGuid(),
                ValidSlug(),
                "taxprocore.com",
                Guid.NewGuid(),
                DateTime.UtcNow,
                isPrimary: true
            )
            .Value;
        domain.ClearDomainEvents();

        var result = domain.Disable();

        Assert.True(result.IsFailure);
        Assert.Empty(domain.DomainEvents);
    }

    [Fact]
    public void ChangeSubdomain_raises_TenantSubdomainChanged()
    {
        var domain = TenantDomain
            .CreateSubdomain(
                Guid.NewGuid(),
                ValidSlug(),
                "taxprocore.com",
                Guid.NewGuid(),
                DateTime.UtcNow,
                isPrimary: true
            )
            .Value;
        domain.ClearDomainEvents();
        var actingUserId = Guid.NewGuid();
        var newSlug = SubdomainSlug.Create("oficina2").Value;

        var result = domain.ChangeSubdomain(newSlug, "taxprocore.com", actingUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("oficina2.taxprocore.com", domain.Host);
        var evt = Assert.Single(domain.DomainEvents.OfType<TenantSubdomainChanged>());
        Assert.Equal("oficina1.taxprocore.com", evt.OldHost);
        Assert.Equal("oficina2.taxprocore.com", evt.NewHost);
        Assert.Equal(actingUserId, evt.ActingUserId);
    }

    [Fact]
    public void ChangeSubdomain_to_the_same_slug_fails_without_raising_any_event()
    {
        var domain = TenantDomain
            .CreateSubdomain(
                Guid.NewGuid(),
                ValidSlug(),
                "taxprocore.com",
                Guid.NewGuid(),
                DateTime.UtcNow,
                isPrimary: true
            )
            .Value;
        domain.ClearDomainEvents();

        var result = domain.ChangeSubdomain(ValidSlug(), "taxprocore.com");

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugUnchanged", result.Error.Code);
        Assert.Empty(domain.DomainEvents);
    }

    [Fact]
    public void ChangeSubdomain_on_a_custom_hostname_fails()
    {
        var domain = TenantDomain
            .CreateCustomHostname(Guid.NewGuid(), "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        domain.ClearDomainEvents();

        var result = domain.ChangeSubdomain(SubdomainSlug.Create("oficina2").Value, "taxprocore.com");

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.NotSubdomain", result.Error.Code);
        Assert.Empty(domain.DomainEvents);
    }

    [Fact]
    public void ChangeSubdomain_of_a_disabled_domain_fails()
    {
        var domain = TenantDomain
            .CreateSubdomain(
                Guid.NewGuid(),
                ValidSlug(),
                "taxprocore.com",
                Guid.NewGuid(),
                DateTime.UtcNow,
                isPrimary: false
            )
            .Value;
        domain.Disable();
        domain.ClearDomainEvents();

        var result = domain.ChangeSubdomain(SubdomainSlug.Create("oficina2").Value, "taxprocore.com");

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.Disabled", result.Error.Code);
        Assert.Empty(domain.DomainEvents);
    }
}
