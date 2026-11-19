using BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Controllers;
using TaxVision.Auth.Application.CentralLogin.Commands;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Api;

public sealed class MobileAuthControllerTests
{
    [Fact]
    public async Task Credentials_return_only_portal_offices_and_an_ephemeral_reference()
    {
        var tenantId = Guid.NewGuid();
        var sessionRef = Guid.NewGuid();
        var bus = new FakeMessageBus
        {
            InvokeHandler = message =>
            {
                var command = Assert.IsType<DiscoverLoginCommand>(message);
                Assert.Equal(UserAccountKind.Portal, command.AccountKind);
                Assert.True(command.ForceSelection);
                return Result.Success(
                    DiscoverLoginResponse.Selection(
                        sessionRef,
                        [new DiscoverOfficeView(tenantId, "office", "Office", false, true)]
                    )
                );
            },
        };
        var controller = new MobileAuthController(bus);

        var action = await controller.Login(
            new MobileAuthController.MobileLoginRequest("client@example.com", "password"),
            CancellationToken.None
        );

        var ok = Assert.IsType<OkObjectResult>(action);
        var response = Assert.IsType<MobileAuthController.MobileLoginResponse>(ok.Value);
        Assert.True(response.RequiresTenantSelection);
        Assert.Equal(sessionRef, response.DiscoverySessionRef);
        Assert.Equal(tenantId, Assert.Single(response.Offices).TenantId);
        Assert.Null(response.AccessToken);
    }
}
