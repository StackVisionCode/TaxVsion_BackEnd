using System.Reflection;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Calendar.Api.Controllers;
using Xunit;

namespace TaxVision.Calendar.Tests.Api;

/// <summary>
/// La página pública de respuesta. Sin sesión, así que la defensa es el endpoint: límite por IP, el
/// token en el cuerpo (nunca en la URL, que acaba en logs e historial) y nada en caché.
/// </summary>
public sealed class CalendarRsvpControllerTests
{
    private static readonly MethodInfo[] Actions = typeof(CalendarRsvpController).GetMethods(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly
    );

    [Fact]
    public void Is_anonymous_and_every_action_is_limited_by_ip()
    {
        Assert.NotNull(typeof(CalendarRsvpController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(typeof(CalendarRsvpController).GetCustomAttribute<RateLimitExemptAttribute>());
        Assert.NotEmpty(Actions);
        foreach (var action in Actions)
        {
            var limit = action.GetCustomAttribute<EnableRateLimitingAttribute>();
            Assert.Equal(CalendarRsvpController.RateLimitPolicy, limit?.PolicyName);
        }
    }

    [Fact]
    public void The_token_never_travels_in_the_url_and_nothing_is_cached()
    {
        foreach (var action in Actions)
        {
            foreach (var route in action.GetCustomAttributes<HttpMethodAttribute>())
            {
                Assert.Equal("POST", Assert.Single(route.HttpMethods));
                Assert.DoesNotContain("{", route.Template ?? string.Empty);
            }

            var cache = action.GetCustomAttribute<ResponseCacheAttribute>();
            Assert.True(cache?.NoStore);
        }
    }
}
