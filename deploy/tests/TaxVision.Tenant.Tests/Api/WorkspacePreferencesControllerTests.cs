using System.Reflection;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.Tenant.Api.Controllers;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Tests.Api;

public sealed class WorkspacePreferencesControllerTests
{
    private static readonly MethodInfo[] Actions = typeof(WorkspacePreferencesController).GetMethods(
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
    );

    [Fact]
    public void Only_office_staff_reach_the_preferences()
    {
        var allowed = typeof(WorkspacePreferencesController).GetCustomAttribute<AllowActorTypesAttribute>()!;

        Assert.Equal(["TenantAdmin", "TenantEmployee"], allowed.ActorTypes.Select(t => t.ToString()).Order());
        Assert.Equal("team", typeof(WorkspacePreferencesController).GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Fact]
    public void Reads_and_writes_have_their_own_rate_limits()
    {
        Assert.Equal(5, Actions.Length);
        Assert.All(
            Actions,
            action =>
            {
                var expected = action.GetCustomAttribute<HttpGetAttribute>() is null
                    ? "tenant.g.preferences_write"
                    : "tenant.f.preferences_read";
                Assert.Equal(expected, action.GetCustomAttribute<RateLimitAttribute>()!.PolicyName);
            }
        );
    }

    [Fact]
    public void Only_the_office_default_needs_settings_manage()
    {
        Assert.All(
            Actions,
            action =>
            {
                var route = action.GetCustomAttribute<HttpMethodAttribute>()!.Template!;
                var permission = action.GetCustomAttribute<HasPermissionAttribute>();
                if (route.EndsWith("/office-default", StringComparison.Ordinal))
                    Assert.Equal("perm:settings.manage", permission!.Policy);
                else
                    Assert.Null(permission);
            }
        );
        Assert.Equal(2, Actions.Count(a => a.GetCustomAttribute<HasPermissionAttribute>() is not null));
    }

    [Fact]
    public void Routes_take_the_key_and_never_a_token_or_an_identity()
    {
        Assert.All(
            Actions,
            action =>
            {
                var route = action.GetCustomAttribute<HttpMethodAttribute>()!.Template!;
                Assert.Contains("{key}", route, StringComparison.Ordinal);
                Assert.DoesNotContain("{token", route, StringComparison.Ordinal);
                Assert.DoesNotContain("userId", route, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("tenantId", route, StringComparison.OrdinalIgnoreCase);
            }
        );
    }

    [Fact]
    public void Puts_cap_the_body_at_sixteen_kilobytes()
    {
        var puts = Actions.Where(a => a.GetCustomAttribute<HttpPutAttribute>() is not null).ToList();

        Assert.Equal(2, puts.Count);
        Assert.All(puts, put => Assert.NotNull(put.GetCustomAttribute<RequestSizeLimitAttribute>()));
    }

    [Fact]
    public void A_version_conflict_is_409_and_validation_errors_are_400()
    {
        Assert.Equal(409, WorkspacePreferencesController.StatusOf(WorkspacePreferenceErrors.VersionConflict));
        Assert.Equal(400, WorkspacePreferencesController.StatusOf(WorkspacePreferenceErrors.UnknownKey));
        Assert.Equal(400, WorkspacePreferencesController.StatusOf(WorkspacePreferenceErrors.InvalidPayload));
        Assert.Equal(400, WorkspacePreferencesController.StatusOf(WorkspacePreferenceErrors.TooManyItems));
    }
}
