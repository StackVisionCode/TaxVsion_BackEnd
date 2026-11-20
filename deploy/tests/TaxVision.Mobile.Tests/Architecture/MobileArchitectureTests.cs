using System.Reflection;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;
using TaxVision.Mobile.Api.Controllers;

namespace TaxVision.Mobile.Tests.Architecture;

public sealed class MobileArchitectureTests
{
    [Theory]
    [InlineData(typeof(VehiclesController), 5)]
    [InlineData(typeof(TripsController), 12)]
    [InlineData(typeof(MobileStatisticsController), 2)]
    [InlineData(typeof(MileageConfigurationController), 5)]
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

    // Los controllers hablan con Application por el bus; nunca con el DbContext ni los repositorios.
    [Fact]
    public void Controllers_should_not_depend_on_Infrastructure()
    {
        var result = Types
            .InAssembly(typeof(VehiclesController).Assembly)
            .That()
            .ResideInNamespace("TaxVision.Mobile.Api.Controllers")
            .ShouldNot()
            .HaveDependencyOn("TaxVision.Mobile.Infrastructure")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "Controllers must not depend on Infrastructure directly: "
                + string.Join(", ", result.FailingTypeNames ?? [])
        );
    }
}
