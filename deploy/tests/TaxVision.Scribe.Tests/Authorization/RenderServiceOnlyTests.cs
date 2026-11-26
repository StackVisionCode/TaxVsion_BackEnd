using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Scribe.Api.Controllers;
using TaxVision.Scribe.Tests.Architecture;

namespace TaxVision.Scribe.Tests.Authorization;

/// <summary>
/// POST /scribe/render es M2M (lo llama Notification) y toma el TenantId del body: solo un token de
/// servicio puede usarlo. Un operador de plataforma, que hoy pasa cualquier [HasPermission], no.
/// </summary>
public sealed class RenderServiceOnlyTests : IClassFixture<ScribeRoutingHostFactory>
{
    private readonly ScribeRoutingHostFactory _factory;

    public RenderServiceOnlyTests(ScribeRoutingHostFactory factory) => _factory = factory;

    [Fact]
    public void Render_requires_the_service_only_policy()
    {
        var authorize = typeof(RenderController).GetCustomAttributes<AuthorizeAttribute>();

        Assert.Contains(authorize, attribute => attribute.Policy == "ServiceOnly");
    }

    [Fact]
    public async Task A_platform_operator_token_is_rejected_and_a_service_token_is_accepted()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var policy = await provider.GetPolicyAsync("ServiceOnly");
        Assert.NotNull(policy);

        var operatorResult = await authorization.AuthorizeAsync(Principal("PlatformAdmin"), policy);
        var serviceResult = await authorization.AuthorizeAsync(Principal("Service"), policy);

        Assert.False(operatorResult.Succeeded);
        Assert.True(serviceResult.Succeeded);
    }

    private static ClaimsPrincipal Principal(string actorType) =>
        new(
            new ClaimsIdentity(
                [new Claim("actor_type", actorType), new Claim("tenant_id", Guid.NewGuid().ToString())],
                "Bearer"
            )
        );
}
