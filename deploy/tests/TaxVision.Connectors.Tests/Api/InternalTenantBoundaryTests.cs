using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Connectors.Api.Controllers;
using TaxVision.Connectors.Api.Requests;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Api;

/// <summary>
/// Los endpoints M2M reciben el tenant en el cuerpo. Los tokens de servicio se emiten por tenant
/// (client-credentials con el tenant en la solicitud), así que un cuerpo con otro tenant es un cruce
/// y nunca un caso legítimo: antes se usaba el del cuerpo sin compararlo con el del token.
/// </summary>
public sealed class InternalTenantBoundaryTests
{
    private static ControllerContext ServiceTokenFor(Guid tenantId)
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(ClaimNames.ActorType, nameof(ActorType.Service)),
                    new Claim(ClaimNames.TenantId, tenantId.ToString()),
                ],
                authenticationType: "Test"
            )
        );
        return new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } };
    }

    [Fact]
    public async Task VisibleIds_is_forbidden_when_the_body_tenant_is_not_the_token_tenant()
    {
        var bus = new FakeMessageBus();
        var controller = new InternalAccountsController(bus) { ControllerContext = ServiceTokenFor(Guid.NewGuid()) };

        var result = await controller.VisibleIds(
            new VisibleAccountIdsRequest(Guid.NewGuid(), Guid.NewGuid(), IncludeOffice: true),
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task VisibleIds_uses_the_token_tenant_when_it_matches_the_body()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var bus = new FakeMessageBus { InvokeAnyResult = (IReadOnlyList<Guid>)[Guid.NewGuid()] };
        var controller = new InternalAccountsController(bus) { ControllerContext = ServiceTokenFor(tenantId) };

        var result = await controller.VisibleIds(
            new VisibleAccountIdsRequest(tenantId, userId, IncludeOffice: false),
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<ListVisibleAccountIdsQuery>(Assert.Single(bus.Invoked));
        Assert.Equal(tenantId, query.TenantId);
        Assert.Equal(userId, query.UserId);
    }

    [Fact]
    public async Task GetBody_is_forbidden_when_the_body_tenant_is_not_the_token_tenant()
    {
        var bus = new FakeMessageBus();
        var controller = new MessagesController(bus) { ControllerContext = ServiceTokenFor(Guid.NewGuid()) };

        var result = await controller.GetBody(
            "provider-message-1",
            new GetMessageBodyRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task GetAttachment_is_forbidden_when_the_body_tenant_is_not_the_token_tenant()
    {
        var bus = new FakeMessageBus();
        var controller = new MessagesController(bus) { ControllerContext = ServiceTokenFor(Guid.NewGuid()) };

        var result = await controller.GetAttachment(
            "provider-message-1",
            "attachment-1",
            new GetMessageAttachmentRequest(Guid.NewGuid(), Guid.NewGuid(), "return.pdf", 10, null),
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Send_is_forbidden_when_the_body_tenant_is_not_the_token_tenant()
    {
        var bus = new FakeMessageBus();
        var controller = new MessagesController(bus) { ControllerContext = ServiceTokenFor(Guid.NewGuid()) };

        var result = await controller.Send(
            Guid.NewGuid(),
            new SendMessageRequest(
                Guid.NewGuid(),
                "Asunto",
                "<p>Hola</p>",
                "Hola",
                ["cliente@example.com"],
                [],
                [],
                null,
                null,
                null,
                null,
                []
            ),
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(bus.Invoked);
    }
}
