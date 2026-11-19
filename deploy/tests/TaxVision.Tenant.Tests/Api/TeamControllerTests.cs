using System.Reflection;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using TaxVision.Tenant.Api.Controllers;
using TaxVision.Tenant.Application.Team;

namespace TaxVision.Tenant.Tests.Api;

public sealed class TeamControllerTests
{
    [Fact]
    public void Viewing_someone_else_without_permission_is_403_and_bad_filters_are_400()
    {
        Assert.Equal(403, TeamController.StatusOf(TeamErrors.ActivityForbidden));
        Assert.Equal(400, TeamController.StatusOf(TeamErrors.InvalidRange));
        Assert.Equal(400, TeamController.StatusOf(TeamErrors.UnknownVerbFilter));
    }

    [Fact]
    public void Only_office_staff_and_platform_admins_reach_the_team_endpoints()
    {
        var allowed = typeof(TeamController).GetCustomAttribute<AllowActorTypesAttribute>()!;

        Assert.Equal(
            ["PlatformAdmin", "TenantAdmin", "TenantEmployee"],
            allowed.ActorTypes.Select(t => t.ToString()).Order()
        );
        Assert.All(
            typeof(TeamController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            action => Assert.NotNull(action.GetCustomAttribute<RateLimitAttribute>())
        );
    }
}
