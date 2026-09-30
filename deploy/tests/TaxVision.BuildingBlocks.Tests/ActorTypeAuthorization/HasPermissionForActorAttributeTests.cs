using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization;

/// <summary>
/// Un endpoint compartido entre staff y portal no puede apilar dos <c>[HasPermission]</c>: ASP.NET
/// exige todas las policies y el staff quedaría fuera. Lo que se fija acá es que el chequeo extra
/// solo alcanza al actor declarado y a nadie más.
/// </summary>
[Collection(AuthorizationMetricsCollection.Name)]
public sealed class HasPermissionForActorAttributeTests
{
    private const string Permission = "portal.folders.view";

    [Fact]
    public async Task The_declared_actor_without_the_permission_is_forbidden()
    {
        var context = BuildContext(ActorType.CustomerPortal, hasPermission: false);

        await Attribute().OnAuthorizationAsync(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public async Task The_declared_actor_with_the_permission_passes()
    {
        var context = BuildContext(ActorType.CustomerPortal, hasPermission: true);

        await Attribute().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    [Theory]
    [InlineData(ActorType.TenantEmployee)]
    [InlineData(ActorType.TenantAdmin)]
    [InlineData(ActorType.PlatformAdmin)]
    public async Task Any_other_actor_is_not_checked_at_all(ActorType actorType)
    {
        var source = new RecordingPermissionsSource(hasPermission: false);
        var context = BuildContext(actorType, source);

        await Attribute().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        Assert.False(source.WasAsked);
    }

    private static HasPermissionForActorAttribute Attribute() => new(ActorType.CustomerPortal, Permission);

    private static AuthorizationFilterContext BuildContext(ActorType actorType, bool hasPermission) =>
        BuildContext(actorType, new RecordingPermissionsSource(hasPermission));

    private static AuthorizationFilterContext BuildContext(ActorType actorType, IUserPermissionsSource source)
    {
        var services = new ServiceCollection();
        services.AddSingleton(source);
        services.AddSingleton<AuthorizationMetrics>();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimNames.ActorType, actorType.ToString())], authenticationType: "Test")
            ),
        };

        return new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            []
        );
    }

    private sealed class RecordingPermissionsSource(bool hasPermission) : IUserPermissionsSource
    {
        public bool WasAsked { get; private set; }

        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default)
        {
            WasAsked = true;
            return Task.FromResult(hasPermission && permission == Permission);
        }
    }
}
