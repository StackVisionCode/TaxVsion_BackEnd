using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using TaxVision.Calendar.Api.Controllers;
using Xunit;

namespace TaxVision.Calendar.Tests.Api;

/// <summary>La ruta del portal es solo del cliente, y cada acción pide un permiso de portal.</summary>
public sealed class PortalCalendarControllerTests
{
    [Fact]
    public void Only_the_customer_portal_actor_reaches_it_and_every_action_needs_a_portal_permission()
    {
        var actors = typeof(PortalCalendarController).GetCustomAttribute<AllowActorTypesAttribute>();
        Assert.NotNull(actors);
        Assert.Equal([ActorType.CustomerPortal], actors.ActorTypes);

        var actions = typeof(PortalCalendarController).GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly
        );
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var permission = action.GetCustomAttribute<HasPermissionAttribute>();
            Assert.Contains(
                permission?.Policy,
                new[] { "perm:" + CalendarPermissions.PortalRead, "perm:" + CalendarPermissions.PortalBook }
            );
        }
    }
}
