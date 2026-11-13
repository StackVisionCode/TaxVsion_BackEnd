using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.Calendar.Api.Controllers;
using TaxVision.Calendar.Domain.PublicBookings;
using Xunit;

namespace TaxVision.Calendar.Tests.Api;

/// <summary>Las páginas públicas y sus demos son de la plataforma: ninguna acción se abre a una oficina.</summary>
public sealed class PlatformBookingPagesControllerTests
{
    private static readonly Type Controller = typeof(PlatformBookingPagesController);

    private static List<MethodInfo> Actions() =>
        [
            .. Controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()),
        ];

    [Fact]
    public void Only_a_platform_admin_gets_in()
    {
        var allowed = Controller.GetCustomAttribute<AllowActorTypesAttribute>();

        Assert.NotNull(allowed);
        Assert.Equal([ActorType.PlatformAdmin], allowed.ActorTypes);
    }

    [Fact]
    public void No_action_is_anonymous_widens_the_actors_or_skips_the_rate_limit()
    {
        var actions = Actions();

        Assert.Equal(16, actions.Count);
        foreach (var action in actions)
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Null(action.GetCustomAttribute<AllowActorTypesAttribute>());
            Assert.NotNull(action.GetCustomAttribute<RateLimitAttribute>());
        }
    }

    [Fact]
    public void Errors_map_to_stable_statuses()
    {
        Assert.Equal(
            StatusCodes.Status404NotFound,
            PlatformBookingPagesController.StatusOf(BookingPageErrors.NotFound)
        );
        Assert.Equal(
            StatusCodes.Status404NotFound,
            PlatformBookingPagesController.StatusOf(PublicBookingErrors.NotFound)
        );
        Assert.Equal(
            StatusCodes.Status409Conflict,
            PlatformBookingPagesController.StatusOf(BookingPageErrors.SlugTaken)
        );
        Assert.Equal(
            StatusCodes.Status409Conflict,
            PlatformBookingPagesController.StatusOf(PublicBookingErrors.NotFinishedYet)
        );
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            PlatformBookingPagesController.StatusOf(BookingPageErrors.TenantNotAllowed)
        );
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            PlatformBookingPagesController.StatusOf(BookingPageErrors.NoActiveHost)
        );
    }
}
