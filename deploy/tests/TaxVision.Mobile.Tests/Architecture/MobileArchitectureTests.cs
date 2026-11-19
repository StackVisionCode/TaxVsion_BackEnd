using System.Reflection;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.Mobile.Api.Controllers;

namespace TaxVision.Mobile.Tests.Architecture;

public sealed class MobileArchitectureTests
{
    [Theory]
    [InlineData(typeof(VehiclesController), 5)]
    [InlineData(typeof(TripsController), 12)]
    [InlineData(typeof(MobileStatisticsController), 2)]
    [InlineData(typeof(MileageConfigurationController), 3)]
    public void Every_vehicle_action_declares_permission_actor_and_registered_rate_limit(Type type, int count)
    {
        var actions = type.GetMethods().Where(x => x.GetCustomAttributes<HttpMethodAttribute>().Any()).ToArray();
        Assert.Equal(count, actions.Length);
        foreach (var action in actions)
        {
            Assert.True(
                action.GetCustomAttribute<AllowActorTypesAttribute>() is not null
                    || type.GetCustomAttribute<AllowActorTypesAttribute>() is not null
            );
            Assert.NotNull(action.GetCustomAttribute<HasPermissionAttribute>());
            var rate = Assert.IsType<RateLimitAttribute>(action.GetCustomAttribute<RateLimitAttribute>());
            Assert.NotNull(RateLimitPolicyCatalog.GetByName(rate.PolicyName));
        }
    }
}
