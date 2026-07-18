using BuildingBlocks.Results;
using TaxVision.Auth.Application.TenantDomains.Commands;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.TenantDomains.Events;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Fase A5/A7 — deshabilitar dominio: anti subdomain-takeover, regla de dominio primario,
/// y que la mutación encole el domain event correcto. La auditoría y el integration
/// event ya no se prueban acá: los produce TenantDomainDisabledHandler cuando
/// AuthDbContext.SaveChangesAsync los despacha (ver TenantDomainDisabledHandlerTests).
/// </summary>
public sealed class DisableTenantDomainHandlerTests
{
    private static async Task<(Result result, FakeUnitOfWork unitOfWork)> DisableAsync(
        FakeTenantDomainRepository domains,
        FakeCloudflareProvisioningClient cloudflare,
        DisableTenantDomainCommand command
    )
    {
        var unitOfWork = new FakeUnitOfWork();

        var result = await DisableTenantDomainHandler.Handle(
            command,
            domains,
            cloudflare,
            unitOfWork,
            CancellationToken.None
        );

        return (result, unitOfWork);
    }

    [Fact]
    public async Task Domain_belonging_to_another_tenant_is_not_found()
    {
        var domain = TenantDomain
            .CreateCustomHostname(Guid.NewGuid(), "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);

        var (result, _) = await DisableAsync(
            domains,
            new FakeCloudflareProvisioningClient(),
            new DisableTenantDomainCommand(Guid.NewGuid(), domain.Id, Guid.NewGuid())
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Primary_subdomain_cannot_be_disabled_and_cloudflare_is_never_called()
    {
        var tenantId = Guid.NewGuid();
        var domain = TenantDomain
            .CreateSubdomain(
                tenantId,
                SubdomainSlug.Create("oficina1").Value,
                "taxprocore.com",
                Guid.NewGuid(),
                DateTime.UtcNow,
                isPrimary: true
            )
            .Value;
        domain.ClearDomainEvents();
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);
        var cloudflare = new FakeCloudflareProvisioningClient();

        var (result, _) = await DisableAsync(
            domains,
            cloudflare,
            new DisableTenantDomainCommand(tenantId, domain.Id, Guid.NewGuid())
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.PrimaryCannotBeDisabled", result.Error.Code);
        Assert.False(cloudflare.DeleteCalled); // el subdominio no tiene recurso en Cloudflare
        Assert.Equal(TenantDomainStatus.Active, domain.Status);
        Assert.Empty(domain.DomainEvents);
    }

    [Fact]
    public async Task Custom_hostname_is_deprovisioned_and_raises_TenantDomainDisabled()
    {
        var tenantId = Guid.NewGuid();
        var domain = TenantDomain
            .CreateCustomHostname(tenantId, "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        domain.MarkProvisioning("cf-1", "cname");
        domain.ClearDomainEvents();
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);
        var cloudflare = new FakeCloudflareProvisioningClient();
        var actingUserId = Guid.NewGuid();

        var (result, unitOfWork) = await DisableAsync(
            domains,
            cloudflare,
            new DisableTenantDomainCommand(tenantId, domain.Id, actingUserId)
        );

        Assert.True(result.IsSuccess);
        Assert.True(cloudflare.DeleteCalled);
        Assert.Equal(TenantDomainStatus.Disabled, domain.Status);
        var evt = Assert.Single(domain.DomainEvents.OfType<TenantDomainDisabled>());
        Assert.Equal(actingUserId, evt.ActingUserId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Cloudflare_delete_failure_leaves_the_domain_enabled_and_raises_no_event()
    {
        var tenantId = Guid.NewGuid();
        var domain = TenantDomain
            .CreateCustomHostname(tenantId, "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        domain.MarkProvisioning("cf-1", "cname");
        domain.ClearDomainEvents();
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);
        var cloudflare = new FakeCloudflareProvisioningClient
        {
            DeleteResult = Result.Failure(new Error("TenantDomain.CloudflareHttp", "boom")),
        };

        var (result, unitOfWork) = await DisableAsync(
            domains,
            cloudflare,
            new DisableTenantDomainCommand(tenantId, domain.Id, Guid.NewGuid())
        );

        Assert.True(result.IsFailure);
        Assert.Equal(TenantDomainStatus.Provisioning, domain.Status);
        Assert.Empty(domain.DomainEvents);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
