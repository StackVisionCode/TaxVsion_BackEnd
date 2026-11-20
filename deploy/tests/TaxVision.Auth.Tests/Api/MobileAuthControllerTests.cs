using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Controllers;
using TaxVision.Auth.Application.CentralLogin.Commands;
using TaxVision.Auth.Application.Sessions.Commands;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// El controller móvil no tiene lógica: convierte la petición en un solo mensaje. Y todo lo de la app viaja con
/// superficie Mobile, para que un token web nunca se renueve ni se tome por la ruta móvil.
/// </summary>
public sealed class MobileAuthControllerTests
{
    [Fact]
    public async Task Login_hands_the_whole_request_to_one_use_case()
    {
        var tenantId = Guid.NewGuid();
        var reference = Guid.NewGuid();
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ =>
                Result.Success(
                    MobileSignInResponse.ForSelection(
                        reference,
                        [new MobileOffice(tenantId, "Office", "office", false)]
                    )
                ),
        };

        var action = await new MobileAuthController(bus).Login(
            new MobileSignInCommand("client@example.com", "password", DeviceName: "iPhone"),
            CancellationToken.None
        );

        var command = Assert.IsType<MobileSignInCommand>(Assert.Single(bus.Invoked));
        Assert.Equal("client@example.com", command.Email);
        Assert.Equal("iPhone", command.DeviceName);
        var response = Assert.IsType<MobileSignInResponse>(Assert.IsType<OkObjectResult>(action).Value);
        Assert.True(response.RequiresTenantSelection);
        Assert.Equal(tenantId, Assert.Single(response.Offices).TenantId);
    }

    [Fact]
    public async Task Login_maps_a_failure_to_its_status()
    {
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ => Result.Failure<MobileSignInResponse>(new Error("Auth.Invalid", "no")),
        };

        var action = await new MobileAuthController(bus).Login(
            new MobileSignInCommand("a@b.com", "x"),
            CancellationToken.None
        );

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ObjectResult>(action).StatusCode);
    }

    [Fact]
    public async Task Refresh_only_rotates_the_mobile_chain()
    {
        var bus = new FakeMessageBus { InvokeHandler = _ => Result.Success(new AuthTokensResponse("a", "r", 900)) };

        await new MobileAuthController(bus).Refresh(
            new MobileAuthController.MobileRefreshRequest("refresh-token"),
            CancellationToken.None
        );

        var command = Assert.IsType<RefreshAccessTokenCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(SessionSurface.Mobile, command.Surface);
        Assert.Null(command.ResolvedTenantId);
    }

    // App de soporte de plataforma: login clásico (clave + bloqueo por cuenta + MFA), sesión del canal móvil.
    [Fact]
    public async Task Platform_login_opens_a_mobile_session_for_the_host_tenant()
    {
        var platform = BuildingBlocks.Tenancy.PlatformTenant.Id;
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ => Result.Success(LoginResponse.ForTokens(new AuthTokensResponse("a", "r", 900))),
        };
        var host = new TaxVision.Auth.Api.Common.ResolvedTenantContext();
        host.SetResolvedTenant(platform);

        await new MobileAuthController(bus).PlatformLogin(
            new MobileAuthController.PlatformLoginRequest("admin@taxoffix.io", "secret", "Pixel"),
            host,
            Microsoft.Extensions.Options.Options.Create(
                new TaxVision.Auth.Application.TenantDomains.TenantDomainOptions { EnforceHostResolution = true }
            ),
            CancellationToken.None
        );

        var command = Assert.IsType<LoginCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(platform, command.TenantId);
        Assert.Equal(SessionSurface.Mobile, command.Surface);
        Assert.Equal(TaxVision.Auth.Domain.Users.UserAccountKind.Staff, command.AccountKind);
    }

    [Fact]
    public async Task Platform_mfa_is_completed_on_the_mobile_surface()
    {
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ =>
                Result.Success(
                    TaxVision.Auth.Application.Mfa.Commands.MfaVerifyResponse.ForTokens(
                        new AuthTokensResponse("a", "r", 900)
                    )
                ),
        };

        await new MobileAuthController(bus).PlatformVerifyMfa(
            new MobileAuthController.PlatformMfaVerifyRequest("ticket", "123456"),
            CancellationToken.None
        );

        var command = Assert.IsType<TaxVision.Auth.Application.Mfa.Commands.VerifyMfaChallengeCommand>(
            Assert.Single(bus.Invoked)
        );
        Assert.Equal("ticket", command.LoginTicket);
        Assert.Equal(SessionSurface.Mobile, command.Surface);
    }

    // El paso 2 web toma el comando del body: un "surface" enviado por el cliente nunca abre una sesión móvil.
    [Fact]
    public async Task The_web_mfa_step_ignores_a_surface_sent_in_the_body()
    {
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ =>
                Result.Success(
                    TaxVision.Auth.Application.Mfa.Commands.MfaVerifyResponse.ForTokens(
                        new AuthTokensResponse("a", "r", 900)
                    )
                ),
        };

        await new MfaController(bus).Verify(
            new TaxVision.Auth.Application.Mfa.Commands.VerifyMfaChallengeCommand(
                "ticket",
                "123456",
                null,
                Surface: SessionSurface.Mobile
            ),
            CancellationToken.None
        );

        var command = Assert.IsType<TaxVision.Auth.Application.Mfa.Commands.VerifyMfaChallengeCommand>(
            Assert.Single(bus.Invoked)
        );
        Assert.Equal(SessionSurface.Workspace, command.Surface);
    }

    // El login central web admite CRM/portal y Account; la superficie de las apps no entra por esa puerta.
    [Fact]
    public async Task The_web_central_login_refuses_the_mobile_surface()
    {
        var bus = new FakeMessageBus();

        var action = await new CentralLoginController(bus).DiscoverLogin(
            new CentralLoginController.DiscoverLoginRequest("a@b.com", "x", Surface: SessionSurface.Mobile),
            CancellationToken.None
        );

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ObjectResult>(action).StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Takeover_is_confirmed_on_the_mobile_surface()
    {
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ => Result.Success(LoginResponse.ForTokens(new AuthTokensResponse("a", "r", 900))),
        };
        var ticket = Guid.NewGuid();

        await new MobileAuthController(bus).Takeover(
            new MobileAuthController.MobileTakeoverRequest(ticket, "iPhone"),
            CancellationToken.None
        );

        var command = Assert.IsType<TakeoverSessionCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(ticket, command.Ticket);
        Assert.Equal(SessionSurface.Mobile, command.Surface);
    }
}
