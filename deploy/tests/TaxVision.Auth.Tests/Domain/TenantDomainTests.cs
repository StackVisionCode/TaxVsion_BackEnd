using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>Fase A2 — ciclo de vida del agregado TenantDomain.</summary>
public sealed class TenantDomainTests
{
    private static SubdomainSlug ValidSlug() => SubdomainSlug.Create("oficina1").Value;

    [Fact]
    public void CreateSubdomain_composes_the_full_host_and_starts_active()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var result = TenantDomain.CreateSubdomain(tenantId, ValidSlug(), "taxprocore.com", Guid.NewGuid(), now);

        Assert.True(result.IsSuccess);
        var domain = result.Value;
        Assert.Equal("oficina1.taxprocore.com", domain.Host);
        Assert.Equal(TenantDomainType.Subdomain, domain.DomainType);
        Assert.Equal(TenantDomainStatus.Active, domain.Status); // wildcard ya cubre TLS, sin provisioning
        Assert.True(domain.IsPrimary);
        Assert.Equal(tenantId, domain.TenantId);
    }

    [Fact]
    public void CreateSubdomain_requires_a_tenant()
    {
        var result = TenantDomain.CreateSubdomain(
            Guid.Empty,
            ValidSlug(),
            "taxprocore.com",
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.Tenant", result.Error.Code);
    }

    [Fact]
    public void CreateCustomHostname_starts_pending_and_is_never_primary()
    {
        var result = TenantDomain.CreateCustomHostname(
            Guid.NewGuid(),
            "archivos.suoficina.com",
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantDomainStatus.Pending, result.Value.Status);
        Assert.False(result.Value.IsPrimary);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nohtld")]
    public void CreateCustomHostname_rejects_invalid_hosts(string host)
    {
        var result = TenantDomain.CreateCustomHostname(Guid.NewGuid(), host, Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.HostInvalid", result.Error.Code);
    }

    [Fact]
    public void Subdomain_cannot_start_provisioning_only_custom_hostnames_can()
    {
        var domain = TenantDomain
            .CreateSubdomain(Guid.NewGuid(), ValidSlug(), "taxprocore.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;

        var result = domain.MarkProvisioning("cf-id-123", "http");

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.NotCustomHostname", result.Error.Code);
    }

    [Fact]
    public void CustomHostname_transitions_pending_to_provisioning_to_active()
    {
        var domain = TenantDomain
            .CreateCustomHostname(Guid.NewGuid(), "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;

        var provisioning = domain.MarkProvisioning("cf-id-123", "http");
        Assert.True(provisioning.IsSuccess);
        Assert.Equal(TenantDomainStatus.Provisioning, domain.Status);

        var active = domain.MarkActive(DateTime.UtcNow);
        Assert.True(active.IsSuccess);
        Assert.Equal(TenantDomainStatus.Active, domain.Status);
        Assert.NotNull(domain.VerifiedAtUtc);
    }

    [Fact]
    public void Disabled_domain_cannot_be_marked_active_again()
    {
        var domain = TenantDomain
            .CreateCustomHostname(Guid.NewGuid(), "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        domain.Disable();

        var result = domain.MarkActive(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.Disabled", result.Error.Code);
    }

    [Fact]
    public void Primary_domain_cannot_be_disabled()
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

        var result = domain.Disable();

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.PrimaryCannotBeDisabled", result.Error.Code);
    }

    [Fact]
    public void Non_primary_domain_can_be_disabled()
    {
        var domain = TenantDomain
            .CreateCustomHostname(Guid.NewGuid(), "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;

        var result = domain.Disable();

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantDomainStatus.Disabled, domain.Status);
    }
}

/// <summary>Fase A2 — reserva temporal de subdominio durante el alta de una oficina.</summary>
public sealed class TenantSubdomainReservationTests
{
    private static SubdomainSlug ValidSlug() => SubdomainSlug.Create("oficina1").Value;

    [Fact]
    public void Create_sets_expiration_from_ttl()
    {
        var now = DateTime.UtcNow;
        var result = TenantSubdomainReservation.Create(
            ValidSlug(),
            "admin@oficina1.com",
            now,
            TimeSpan.FromMinutes(15)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(now.AddMinutes(15), result.Value.ExpiresAtUtc);
        Assert.True(result.Value.IsActive(now));
    }

    [Fact]
    public void Create_rejects_invalid_email()
    {
        var result = TenantSubdomainReservation.Create(
            ValidSlug(),
            "not-an-email",
            DateTime.UtcNow,
            TimeSpan.FromMinutes(15)
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.ReservationEmail", result.Error.Code);
    }

    [Fact]
    public void Is_expired_after_ttl_elapses()
    {
        var now = DateTime.UtcNow;
        var reservation = TenantSubdomainReservation
            .Create(ValidSlug(), "admin@oficina1.com", now, TimeSpan.FromMinutes(15))
            .Value;

        Assert.False(reservation.IsExpired(now.AddMinutes(10)));
        Assert.True(reservation.IsExpired(now.AddMinutes(16)));
    }

    [Fact]
    public void Consume_marks_it_used_and_cannot_be_consumed_twice()
    {
        var now = DateTime.UtcNow;
        var reservation = TenantSubdomainReservation
            .Create(ValidSlug(), "admin@oficina1.com", now, TimeSpan.FromMinutes(15))
            .Value;

        var first = reservation.Consume(now.AddMinutes(1));
        Assert.True(first.IsSuccess);

        var second = reservation.Consume(now.AddMinutes(2));
        Assert.True(second.IsFailure);
        Assert.Equal("TenantDomain.ReservationConsumed", second.Error.Code);
    }

    [Fact]
    public void Consume_fails_once_expired()
    {
        var now = DateTime.UtcNow;
        var reservation = TenantSubdomainReservation
            .Create(ValidSlug(), "admin@oficina1.com", now, TimeSpan.FromMinutes(15))
            .Value;

        var result = reservation.Consume(now.AddMinutes(20));

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.ReservationExpired", result.Error.Code);
    }
}
