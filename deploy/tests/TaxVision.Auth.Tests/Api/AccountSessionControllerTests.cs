using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Api.Controllers;
using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Tests.Api;

public sealed class AccountSessionControllerTests
{
    private static readonly string[] CookieEndpoints =
    [
        nameof(AccountSessionController.FromHandoff),
        nameof(AccountSessionController.FromLogin),
        nameof(AccountSessionController.Takeover),
        nameof(AccountSessionController.Refresh),
        nameof(AccountSessionController.Logout),
    ];

    [Fact]
    public void The_handoff_is_for_tenant_admins_of_the_workspace_only()
    {
        var method = Action(nameof(AccountSessionController.Handoff));

        Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(), attribute => attribute.Policy is null);
        Assert.Equal(
            new[] { ActorType.TenantAdmin },
            method.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );
        Assert.Equal(
            HasPermissionAttribute.PolicyPrefix + PermissionCatalog.BillingView,
            method.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
        // Sin [AllowSurface]: un token del Account no puede pedir otro vale.
        Assert.Null(method.GetCustomAttribute<AllowSurfaceAttribute>());
    }

    [Fact]
    public void Every_cookie_endpoint_checks_the_origin_and_is_rate_limited()
    {
        foreach (var name in CookieEndpoints)
        {
            var method = Action(name);
            Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.NotNull(method.GetCustomAttribute<RequireAccountOriginAttribute>());
            Assert.Equal("auth-account-session", method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        }
    }

    [Fact]
    public void The_refresh_token_never_travels_in_a_response_body()
    {
        var properties = typeof(AccountSessionController.AccountSessionResponse).GetProperties().Select(p => p.Name);

        Assert.DoesNotContain("RefreshToken", properties);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://evil.example.com")]
    public void A_request_without_an_allowed_origin_is_forbidden(string? origin)
    {
        var context = FilterContext(origin);

        new RequireAccountOriginAttribute().OnAuthorization(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal("Auth.OriginNotAllowed", Assert.IsType<Error>(result.Value).Code);
    }

    [Fact]
    public void The_landing_origin_is_allowed()
    {
        var context = FilterContext("https://taxproffice.com/");

        new RequireAccountOriginAttribute().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void The_refresh_cookie_is_host_only_http_only_and_strict()
    {
        var httpContext = new DefaultHttpContext();

        AccountSessionCookie.Append(httpContext.Response, "raw-refresh", DateTime.UtcNow.AddDays(14));

        var header = httpContext.Response.Headers.SetCookie.ToString();
        Assert.StartsWith("__Host-tv-account-rt=raw-refresh", header);
        Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Only_the_expected_auth_actions_accept_account_tokens()
    {
        var allowed = SurfaceActions().Select(method => $"{method.DeclaringType!.Name}.{method.Name}").ToArray();

        Assert.Equal(
            new[] { "AuthController.Me", "AuthController.Reauthenticate", "UsersController.GetTenantLimits" },
            allowed
        );
    }

    /// <summary>
    /// Confirmar la contraseña es un intento de credencial: va en la categoría de credenciales (B), no en la
    /// de lectura. Un step-up con el límite de un GET sería un oráculo de contraseñas.
    /// </summary>
    [Fact]
    public void Confirming_the_password_is_limited_like_a_credential_attempt()
    {
        var policy = typeof(AuthController)
            .GetMethod(nameof(AuthController.Reauthenticate))!
            .GetCustomAttribute<RateLimitAttribute>()!
            .PolicyName;

        Assert.Equal(RateLimitCategory.B, RateLimitPolicyCatalog.GetByName(policy).Category);
    }

    /// <summary>
    /// El tenant sale siempre del token. Si un endpoint del Account empezara a aceptarlo por la ruta, la
    /// query o el body, un admin podría pedir la oficina de otro con solo cambiar el identificador.
    /// </summary>
    [Fact]
    public void No_account_endpoint_takes_the_tenant_from_the_caller()
    {
        foreach (var action in SurfaceActions())
        {
            var name = $"{action.DeclaringType!.Name}.{action.Name}";
            foreach (var parameter in action.GetParameters())
            {
                Assert.False(
                    parameter.Name!.Contains("tenant", StringComparison.OrdinalIgnoreCase),
                    $"{name} recibe el tenant de quien llama: {parameter.Name}."
                );

                if (parameter.ParameterType.Namespace?.StartsWith("TaxVision", StringComparison.Ordinal) != true)
                    continue;

                var carried = parameter
                    .ParameterType.GetProperties()
                    .FirstOrDefault(property => property.Name.Contains("tenant", StringComparison.OrdinalIgnoreCase));
                Assert.True(carried is null, $"{name} recibe el tenant dentro de {parameter.ParameterType.Name}.");
            }
        }
    }

    /// <summary>Acciones de Auth que aceptan un token del Account.</summary>
    private static MethodInfo[] SurfaceActions() =>
        [
            .. typeof(AuthController)
                .Assembly.GetTypes()
                .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
                .SelectMany(type =>
                    type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                )
                .Where(method =>
                    method.GetCustomAttribute<AllowSurfaceAttribute>() is { } attribute
                        && attribute.Surfaces.Contains(AccessSurface.Account)
                    || method.DeclaringType!.GetCustomAttribute<AllowSurfaceAttribute>() is not null
                )
                .OrderBy(method => $"{method.DeclaringType!.Name}.{method.Name}", StringComparer.Ordinal),
        ];

    private static MethodInfo Action(string name) => typeof(AccountSessionController).GetMethod(name)!;

    private static AuthorizationFilterContext FilterContext(string? origin)
    {
        var services = new ServiceCollection()
            .AddSingleton(
                Options.Create(
                    new AccountSessionOptions
                    {
                        AllowedOrigins = ["https://taxproffice.com", "https://www.taxproffice.com"],
                    }
                )
            )
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        if (origin is not null)
            httpContext.Request.Headers.Origin = origin;

        return new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            []
        );
    }
}
