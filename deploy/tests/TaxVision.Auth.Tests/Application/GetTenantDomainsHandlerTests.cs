using TaxVision.Auth.Application.TenantDomains.Queries;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase A5 — listar solo devuelve dominios del tenant pedido.</summary>
public sealed class GetTenantDomainsHandlerTests
{
    [Fact]
    public async Task Only_returns_domains_of_the_requested_tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var domains = new FakeTenantDomainRepository();
        domains.Seed(
            TenantDomain
                .CreateSubdomain(
                    tenantA,
                    SubdomainSlug.Create("oficina1").Value,
                    "taxprocore.com",
                    Guid.NewGuid(),
                    DateTime.UtcNow
                )
                .Value
        );
        domains.Seed(
            TenantDomain
                .CreateSubdomain(
                    tenantB,
                    SubdomainSlug.Create("oficina2").Value,
                    "taxprocore.com",
                    Guid.NewGuid(),
                    DateTime.UtcNow
                )
                .Value
        );

        var result = await GetTenantDomainsHandler.Handle(
            new GetTenantDomainsQuery(tenantA),
            domains,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var domain = Assert.Single(result.Value);
        Assert.Equal("oficina1.taxprocore.com", domain.Host);
    }
}
