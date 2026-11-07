using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Api.Controllers;
using TaxVision.Connectors.Application.Common;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.OAuth;
using Xunit;

namespace TaxVision.Connectors.Tests.OAuth;

public sealed class OAuthCallbackControllerTests
{
    // CON-18: el navegador siempre vuelve al CRM; un choque de concurrencia no se muestra como JSON.
    [Fact]
    public async Task Conflict_during_the_exchange_redirects_with_an_error()
    {
        var states = new InMemoryOAuthConnectStateStore();
        var state = await states.CreateAsync(Guid.NewGuid(), ProviderCode.Gmail, Guid.NewGuid(), "owner@example.test");
        var bus = new FakeMessageBus
        {
            InvokeThrows = new ConflictException("Persistence.ConcurrencyConflict", "The record changed."),
        };
        var controller = new OAuthCallbackController(
            bus,
            states,
            Options.Create(new ConnectorsPortalOptions { BaseUrl = "http://localhost:4200" }),
            new FixedPlatform(),
            NullLogger<OAuthCallbackController>.Instance
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Gmail("code", state, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Contains("connectors_error=", redirect.Url);
    }

    private sealed class FixedPlatform : IPlatformSettings
    {
        public PlatformSettingsSnapshot Current { get; } =
            new(
                1,
                "TaxVision",
                "TaxVision LLC",
                "localhost",
                "api.localhost",
                "support@example.test",
                "billing@example.test"
            );
    }
}
