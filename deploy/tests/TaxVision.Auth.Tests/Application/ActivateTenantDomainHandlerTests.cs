using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains.Commands;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.TenantDomains.Events;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Fase A5/A7 — activación de custom hostname: aislamiento por tenant, gate de Cloudflare,
/// y que la mutación encole el domain event correcto. La auditoría y los integration
/// events ya no se prueban acá: los produce TenantDomainActivatedHandler cuando
/// AuthDbContext.SaveChangesAsync los despacha (ver TenantDomainActivatedHandlerTests).
/// </summary>
public sealed class ActivateTenantDomainHandlerTests
{
    private static TenantDomain ProvisioningDomain(Guid tenantId)
    {
        var domain = TenantDomain
            .CreateCustomHostname(tenantId, "archivos.suoficina.com", Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        domain.MarkProvisioning("cf-1", "cname");
        domain.ClearDomainEvents(); // solo interesan los eventos que dispara la activación
        return domain;
    }

    private static async Task<(
        Result<TaxVision.Auth.Application.TenantDomains.TenantDomainResponse> result,
        FakeUnitOfWork unitOfWork
    )> ActivateAsync(
        FakeTenantDomainRepository domains,
        FakeCloudflareProvisioningClient cloudflare,
        ActivateTenantDomainCommand command
    )
    {
        var unitOfWork = new FakeUnitOfWork();

        var result = await ActivateTenantDomainHandler.Handle(
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
        var domain = ProvisioningDomain(Guid.NewGuid());
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);

        var (result, unitOfWork) = await ActivateAsync(
            domains,
            new FakeCloudflareProvisioningClient(),
            new ActivateTenantDomainCommand(Guid.NewGuid(), domain.Id, Guid.NewGuid()) // otro tenant
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.NotFound", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Not_yet_active_in_cloudflare_fails_without_mutating_or_raising_events()
    {
        var tenantId = Guid.NewGuid();
        var domain = ProvisioningDomain(tenantId);
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);
        var cloudflare = new FakeCloudflareProvisioningClient
        {
            GetResult = Result.Success(new CustomHostnameResult("cf-1", "pending", "pending", null, null, [])),
        };

        var (result, unitOfWork) = await ActivateAsync(
            domains,
            cloudflare,
            new ActivateTenantDomainCommand(tenantId, domain.Id, Guid.NewGuid())
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.NotReadyForActivation", result.Error.Code);
        Assert.Equal(TenantDomainStatus.Provisioning, domain.Status);
        Assert.Empty(domain.DomainEvents);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Fully_active_in_cloudflare_activates_and_raises_TenantDomainActivated()
    {
        var tenantId = Guid.NewGuid();
        var domain = ProvisioningDomain(tenantId);
        var domains = new FakeTenantDomainRepository();
        domains.Seed(domain);
        var cloudflare = new FakeCloudflareProvisioningClient
        {
            GetResult = Result.Success(new CustomHostnameResult("cf-1", "active", "active", null, null, [])),
        };
        var actingUserId = Guid.NewGuid();

        var (result, unitOfWork) = await ActivateAsync(
            domains,
            cloudflare,
            new ActivateTenantDomainCommand(tenantId, domain.Id, actingUserId)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantDomainStatus.Active, domain.Status);
        var evt = Assert.Single(domain.DomainEvents.OfType<TenantDomainActivated>());
        Assert.Equal(actingUserId, evt.ActingUserId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }
}
