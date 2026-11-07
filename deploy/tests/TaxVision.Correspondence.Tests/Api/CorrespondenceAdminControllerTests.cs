using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Api.Controllers;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Tests.Ingest;

namespace TaxVision.Correspondence.Tests.Api;

/// <summary>COR-02: la reparación de hilos mixtos se dispara solo desde la plataforma, y en seco por defecto.</summary>
public sealed class CorrespondenceAdminControllerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly List<object> _invoked = [];

    private CorrespondenceAdminController Controller(ActorType actorType, Guid callerTenantId)
    {
        var bus = new FakeMessageBus
        {
            OnInvoke = message =>
            {
                _invoked.Add(message);
                var command = (RepairCrossCustomerThreadsCommand)message;
                return Task.FromResult<object?>(new RepairCrossCustomerThreadsReport(command.DryRun, 1, 1, 0, 0, 0, []));
            },
        };
        var user = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim("sub", Guid.NewGuid().ToString()),
                    new Claim(ClaimNames.TenantId, callerTenantId.ToString()),
                    new Claim(ClaimNames.ActorType, actorType.ToString()),
                ],
                "test"
            )
        );
        return new CorrespondenceAdminController(bus)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
        };
    }

    [Fact]
    public void OnlyPlatformAdminAndServiceTokensAreAllowed()
    {
        var allowed = typeof(CorrespondenceAdminController).GetCustomAttribute<AllowActorTypesAttribute>()!;

        Assert.Equal([ActorType.PlatformAdmin, ActorType.Service], allowed.ActorTypes.Order());
    }

    [Fact]
    public async Task PlatformAdmin_RunsADryRunByDefault()
    {
        var result = await Controller(ActorType.PlatformAdmin, PlatformTenant.Id)
            .RepairCrossCustomerThreads(_tenantId, ct: CancellationToken.None);

        var report = Assert.IsType<RepairCrossCustomerThreadsReport>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(report.DryRun);
        Assert.Equal(new RepairCrossCustomerThreadsCommand(_tenantId, DryRun: true), Assert.Single(_invoked));
    }

    [Fact]
    public async Task PlatformServiceToken_CanApply()
    {
        var result = await Controller(ActorType.Service, PlatformTenant.Id)
            .RepairCrossCustomerThreads(_tenantId, dryRun: false, ct: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new RepairCrossCustomerThreadsCommand(_tenantId, DryRun: false), Assert.Single(_invoked));
    }

    [Fact]
    public async Task ServiceTokenOfAnotherTenant_IsForbidden()
    {
        var result = await Controller(ActorType.Service, Guid.NewGuid())
            .RepairCrossCustomerThreads(_tenantId, dryRun: false, ct: CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(_invoked);
    }

    [Fact]
    public async Task MissingTenant_IsABadRequest()
    {
        var result = await Controller(ActorType.PlatformAdmin, PlatformTenant.Id)
            .RepairCrossCustomerThreads(Guid.Empty, ct: CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(_invoked);
    }
}
