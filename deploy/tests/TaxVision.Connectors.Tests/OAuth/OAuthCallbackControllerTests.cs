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

    // Cancelar en Google/Microsoft vuelve al subdominio de la oficina, no a app.{dominio}.
    [Fact]
    public async Task ProviderError_WithState_ReturnsToTheTenantOrigin()
    {
        var (controller, states) = Build("https://app.taxoffix.io", "taxoffix.io");
        var state = await states.CreateAsync(
            Guid.NewGuid(),
            ProviderCode.Gmail,
            Guid.NewGuid(),
            returnOrigin: "https://manfer.taxoffix.io"
        );

        var result = await controller.Gmail(null, state, "access_denied", CancellationToken.None);

        Assert.Equal(
            "https://manfer.taxoffix.io?connectors_error=access_denied",
            Assert.IsType<RedirectResult>(result).Url
        );
    }

    [Fact]
    public async Task MissingCode_WithState_ReturnsToTheTenantOrigin()
    {
        var (controller, states) = Build("https://app.taxoffix.io", "taxoffix.io");
        var state = await states.CreateAsync(
            Guid.NewGuid(),
            ProviderCode.Graph,
            Guid.NewGuid(),
            returnOrigin: "https://manfer.taxoffix.io"
        );

        var result = await controller.Graph(null, state, null, null, CancellationToken.None);

        Assert.Equal(
            "https://manfer.taxoffix.io?connectors_error=invalid_request",
            Assert.IsType<RedirectResult>(result).Url
        );
    }

    [Fact]
    public async Task StateOfAnotherProvider_ReturnsToTheTenantOrigin()
    {
        var (controller, states) = Build("https://app.taxoffix.io", "taxoffix.io");
        var state = await states.CreateAsync(
            Guid.NewGuid(),
            ProviderCode.Gmail,
            Guid.NewGuid(),
            returnOrigin: "https://manfer.taxoffix.io"
        );

        var result = await controller.Graph("code", state, null, null, CancellationToken.None);

        Assert.Equal(
            "https://manfer.taxoffix.io?connectors_error=invalid_state",
            Assert.IsType<RedirectResult>(result).Url
        );
    }

    // En local no hay subdominio: vuelve al puerto del front que inició la conexión.
    [Fact]
    public async Task ProviderError_InLocal_ReturnsToTheFrontendPort()
    {
        var (controller, states) = Build("http://localhost:4200", "localhost");
        var state = await states.CreateAsync(
            Guid.NewGuid(),
            ProviderCode.Gmail,
            Guid.NewGuid(),
            returnOrigin: "http://localhost:4300"
        );

        var result = await controller.Gmail(null, state, "access_denied", CancellationToken.None);

        Assert.Equal("http://localhost:4300?connectors_error=access_denied", Assert.IsType<RedirectResult>(result).Url);
    }

    // Un origen que no es del dominio nunca se usa (no es un open redirect).
    [Fact]
    public async Task ProviderError_WithForeignOrigin_FallsBackToTheBaseUrl()
    {
        var (controller, states) = Build("https://app.taxoffix.io", "taxoffix.io");
        var state = await states.CreateAsync(
            Guid.NewGuid(),
            ProviderCode.Gmail,
            Guid.NewGuid(),
            returnOrigin: "https://evil.com"
        );

        var result = await controller.Gmail(null, state, "access_denied", CancellationToken.None);

        Assert.StartsWith("https://app.taxoffix.io?", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public async Task AdminConsent_ReturnsToTheTenantOrigin()
    {
        var (controller, states) = Build("https://app.taxoffix.io", "taxoffix.io");
        var state = await states.CreateAsync(
            Guid.NewGuid(),
            ProviderCode.Graph,
            Guid.NewGuid(),
            returnOrigin: "https://manfer.taxoffix.io"
        );

        var result = await controller.AdminConsentCallback(state, null, "True", CancellationToken.None);

        Assert.Equal(
            "https://manfer.taxoffix.io?connectors_admin_consent=true",
            Assert.IsType<RedirectResult>(result).Url
        );
    }

    private static (OAuthCallbackController Controller, InMemoryOAuthConnectStateStore States) Build(
        string baseUrl,
        string platformDomain
    )
    {
        var states = new InMemoryOAuthConnectStateStore();
        var controller = new OAuthCallbackController(
            new FakeMessageBus(),
            states,
            Options.Create(new ConnectorsPortalOptions { BaseUrl = baseUrl }),
            new TestPlatform(platformDomain),
            NullLogger<OAuthCallbackController>.Instance
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, states);
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
