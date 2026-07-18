using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains.Commands;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase A5 — verify es de solo lectura: nunca muta el TenantDomain.</summary>
public sealed class RequestTenantDomainVerificationHandlerTests
{
    [Fact]
    public async Task Domain_belonging_to_another_tenant_is_not_found()
    {
        var domain = TenantDomain
            .CreateCustomHostname(Guid.NewGuid(), "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        domain.MarkProvisioning("cf-1", "cname");
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);

        var result = await RequestTenantDomainVerificationHandler.Handle(
            new RequestTenantDomainVerificationCommand(Guid.NewGuid(), domain.Id),
            domains,
            new FakeCloudflareProvisioningClient(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Wildcard_subdomain_has_no_cloudflare_resource_to_verify()
    {
        var tenantId = Guid.NewGuid();
        var domain = TenantDomain
            .CreateSubdomain(
                tenantId,
                SubdomainSlug.Create("oficina1").Value,
                "taxprocore.com",
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);

        var result = await RequestTenantDomainVerificationHandler.Handle(
            new RequestTenantDomainVerificationCommand(tenantId, domain.Id),
            domains,
            new FakeCloudflareProvisioningClient(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.NotCustomHostname", result.Error.Code);
    }

    [Fact]
    public async Task Reports_cloudflare_status_without_mutating_the_domain()
    {
        var tenantId = Guid.NewGuid();
        var domain = TenantDomain
            .CreateCustomHostname(tenantId, "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        domain.MarkProvisioning("cf-1", "cname");
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);
        var cloudflare = new FakeCloudflareProvisioningClient
        {
            GetResult = Result.Success(
                new CustomHostnameResult("cf-1", "pending", "pending", "_cf-verify", "abc123", ["TXT _cf = abc"])
            ),
        };

        var result = await RequestTenantDomainVerificationHandler.Handle(
            new RequestTenantDomainVerificationCommand(tenantId, domain.Id),
            domains,
            cloudflare,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("pending", result.Value.CloudflareStatus);
        Assert.Single(result.Value.DcvRecords);
        Assert.Equal(TenantDomainStatus.Provisioning, domain.Status); // sin mutar
    }
}
