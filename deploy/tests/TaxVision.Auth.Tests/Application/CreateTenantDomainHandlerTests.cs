using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.TenantDomains.Events;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Fase A5/A6/A7 — alta de custom hostname. El camino exitoso ya no audita/publica a
/// mano: domain.CreateCustomHostname encola TenantDomainCreated (domain event), que
/// AuthDbContext.SaveChangesAsync despacharía en producción (ver
/// TenantDomainCreatedHandlerTests) — acá solo se prueba que el evento quedó encolado,
/// porque FakeUnitOfWork no dispara ese despacho. El camino de falla de Cloudflare
/// sigue auditando/publicando a mano (el agregado nunca se persiste, ver comentario
/// en CreateTenantDomainHandler.RecordFailureAsync).
/// </summary>
public sealed class CreateTenantDomainHandlerTests
{
    [Fact]
    public async Task Host_already_claimed_fails_without_calling_cloudflare_or_auditing()
    {
        var domains = new FakeTenantDomainRepository { HostTaken = true };
        var cloudflare = new FakeCloudflareProvisioningClient();
        var audit = new FakeAuthAuditWriter();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateTenantDomainHandler.Handle(
            new CreateTenantDomainCommand(Guid.NewGuid(), Guid.NewGuid(), "archivos.suoficina.com"),
            domains,
            cloudflare,
            audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.HostTaken", result.Error.Code);
        Assert.Null(domains.Added);
        Assert.Empty(audit.Logs);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Cloudflare_failure_is_audited_and_published_but_the_domain_is_not_persisted()
    {
        var domains = new FakeTenantDomainRepository();
        var cloudflare = new FakeCloudflareProvisioningClient
        {
            CreateResult = Result.Failure<CustomHostnameResult>(new Error("TenantDomain.CloudflareHttp", "boom")),
        };
        var audit = new FakeAuthAuditWriter();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateTenantDomainHandler.Handle(
            new CreateTenantDomainCommand(Guid.NewGuid(), Guid.NewGuid(), "archivos.suoficina.com"),
            domains,
            cloudflare,
            audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.CloudflareHttp", result.Error.Code);
        Assert.Null(domains.Added);
        Assert.Single(audit.Logs, log => log.Action == AuthAuditAction.TenantDomainProvisioningFailed && !log.Success);
        Assert.Single(bus.Published.OfType<TenantDomainProvisioningFailedIntegrationEvent>());
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Successful_creation_persists_domain_audits_and_publishes_created_event()
    {
        var tenantId = Guid.NewGuid();
        var domains = new FakeTenantDomainRepository();
        var cloudflare = new FakeCloudflareProvisioningClient();
        var audit = new FakeAuthAuditWriter();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateTenantDomainHandler.Handle(
            new CreateTenantDomainCommand(tenantId, Guid.NewGuid(), "Archivos.SuOficina.com"),
            domains,
            cloudflare,
            audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotNull(domains.Added);
        Assert.Equal(tenantId, domains.Added!.TenantId);
        Assert.Equal(TenantDomainStatus.Provisioning, domains.Added.Status);
        Assert.Equal("cf-1", domains.Added.CloudflareCustomHostnameId);
        Assert.Equal("_cf-verify", result.Value.OwnershipTxtName);
        Assert.Empty(audit.Logs); // el éxito ya no audita a mano — ver domain event
        Assert.Empty(bus.Published); // idem — ver domain event
        var evt = Assert.Single(domains.Added!.DomainEvents.OfType<TenantDomainCreated>());
        Assert.Equal(domains.Added.Id, evt.DomainId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }
}
