using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.Subscription.Api.Controllers.Admin;

namespace TaxVision.Subscription.Tests.Architecture;

/// <summary>
/// D13: toda acción de los controladores del catálogo (planes, add-ons, módulos, rate limits y precio de asiento)
/// exige el actor de plataforma Y el permiso <c>subscription.catalog.manage</c>. Uno nuevo sin el par no compila el gate.
/// </summary>
public sealed class CatalogAdminAuthorizationFitnessTests
{
    public static TheoryData<Type> CatalogControllers =>
        [
            typeof(PlansAdminController),
            typeof(AddOnsAdminController),
            typeof(ModulesAdminController),
            typeof(PlanRateLimitsAdminController),
            typeof(SeatsAdminController),
            typeof(CatalogAuditAdminController),
            typeof(DefaultRateLimitProfileAdminController),
            typeof(SubscriberMigrationsAdminController),
        ];

    [Theory]
    [MemberData(nameof(CatalogControllers))]
    public void Every_catalog_action_needs_the_platform_actor_and_the_catalog_permission(Type controller)
    {
        var actions = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();
        Assert.NotEmpty(actions);

        foreach (var action in actions)
        {
            var actors =
                action.GetCustomAttribute<AllowActorTypesAttribute>()
                ?? controller.GetCustomAttribute<AllowActorTypesAttribute>();
            Assert.True(
                actors is not null && actors.ActorTypes.SequenceEqual([ActorType.PlatformAdmin]),
                $"{controller.Name}.{action.Name} must allow only the PlatformAdmin actor."
            );

            var policies = action
                .GetCustomAttributes<HasPermissionAttribute>()
                .Concat(controller.GetCustomAttributes<HasPermissionAttribute>())
                .Select(attribute => attribute.Policy)
                .ToList();
            Assert.Contains(HasPermissionAttribute.PolicyPrefix + SubscriptionPermissions.CatalogManage, policies);
            Assert.DoesNotContain(
                HasPermissionAttribute.PolicyPrefix + SubscriptionPermissions.AdminCrossTenant,
                policies
            );
        }
    }
}
