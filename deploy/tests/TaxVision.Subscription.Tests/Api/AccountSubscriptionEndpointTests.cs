using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.Subscription.Api.Controllers;

namespace TaxVision.Subscription.Tests.Api;

/// <summary>
/// El read model del Account es el primer endpoint de Subscription que acepta tokens del Landing: si alguien
/// afloja actor, permiso o superficie, esto lo frena.
/// </summary>
public sealed class AccountSubscriptionEndpointTests
{
    private static readonly MethodInfo Action = typeof(SubscriptionsController).GetMethod(
        nameof(SubscriptionsController.GetAccountSubscription)
    )!;

    [Fact]
    public void Only_the_office_admin_with_billing_view_reads_the_account()
    {
        Assert.Equal("me/account", Action.GetCustomAttribute<HttpMethodAttribute>()!.Template);
        Assert.Equal(
            new[] { ActorType.TenantAdmin },
            Action.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );
        Assert.Equal(
            HasPermissionAttribute.PolicyPrefix + SubscriptionPermissions.BillingView,
            Action.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
    }

    /// <summary>Acciones de Subscription que aceptan un token del Account.</summary>
    private static MethodInfo[] SurfaceActions() =>
        [
            .. typeof(SubscriptionsController)
                .Assembly.GetTypes()
                .SelectMany(type =>
                    type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                )
                .Where(method => method.GetCustomAttribute<AllowSurfaceAttribute>() is not null)
                .OrderBy(method => $"{method.DeclaringType!.Name}.{method.Name}", StringComparer.Ordinal),
        ];

    [Fact]
    public void Only_the_read_model_and_the_purchase_flows_accept_account_tokens()
    {
        Assert.Contains(AccessSurface.Account, Action.GetCustomAttribute<AllowSurfaceAttribute>()!.Surfaces);

        var allowed = SurfaceActions().Select(method => $"{method.DeclaringType!.Name}.{method.Name}").ToArray();

        // El Account lee la suscripción, compra asientos y add-ons, cambia de plan y cancela/reanuda; nada
        // más de Subscription acepta su token.
        Assert.Equal(
            new[]
            {
                "AddOnsController.GetCheckoutStatus",
                "AddOnsController.StartCheckout",
                "SeatsController.GetCheckoutStatus",
                "SeatsController.GetQuote",
                "SeatsController.GetSeats",
                "SeatsController.StartCheckout",
                "SubscriptionsController.Cancel",
                "SubscriptionsController.CancelPendingPlanChange",
                "SubscriptionsController.ChangePlan",
                "SubscriptionsController.GetAccountSubscription",
                "SubscriptionsController.GetRenewCheckoutStatus",
                "SubscriptionsController.PreviewPlanChange",
                "SubscriptionsController.Resume",
                "SubscriptionsController.StartRenewCheckout",
            },
            allowed
        );
    }

    /// <summary>
    /// Lo que puede arrancar un cobro va en la categoría L, que es ventana fija y corta: es el freno a un
    /// token del Account que intente disparar compras en ráfaga. El resto son lecturas (F) o gestión (G).
    /// </summary>
    [Fact]
    public void Whatever_can_start_a_charge_is_limited_as_a_charge()
    {
        string[] charges =
        [
            "AddOnsController.StartCheckout",
            "SeatsController.StartCheckout",
            "SubscriptionsController.ChangePlan",
            "SubscriptionsController.StartRenewCheckout",
        ];

        foreach (var action in SurfaceActions())
        {
            var name = $"{action.DeclaringType!.Name}.{action.Name}";
            var attribute = action.GetCustomAttribute<RateLimitAttribute>();
            Assert.True(attribute is not null, $"{name} acepta tokens del Account sin política de rate limit.");

            var category = RateLimitPolicyCatalog.GetByName(attribute!.PolicyName).Category;
            if (charges.Contains(name))
                Assert.Equal(RateLimitCategory.L, category);
            else
                Assert.Contains(category, new[] { RateLimitCategory.F, RateLimitCategory.G });
        }
    }

    /// <summary>
    /// El tenant sale siempre del token. Si un endpoint del Account empezara a aceptarlo por la ruta, la
    /// query o el body, un admin podría pedir la oficina de otro con solo cambiar el identificador.
    /// </summary>
    [Fact]
    public void No_account_endpoint_takes_the_tenant_from_the_caller()
    {
        foreach (var action in SurfaceActions())
        {
            var name = $"{action.DeclaringType!.Name}.{action.Name}";
            foreach (var parameter in action.GetParameters())
            {
                Assert.False(
                    parameter.Name!.Contains("tenant", StringComparison.OrdinalIgnoreCase),
                    $"{name} recibe el tenant de quien llama: {parameter.Name}."
                );

                if (parameter.ParameterType.Namespace?.StartsWith("TaxVision", StringComparison.Ordinal) != true)
                    continue;

                var carried = parameter
                    .ParameterType.GetProperties()
                    .FirstOrDefault(property => property.Name.Contains("tenant", StringComparison.OrdinalIgnoreCase));
                Assert.True(carried is null, $"{name} recibe el tenant dentro de {parameter.ParameterType.Name}.");
            }
        }
    }
}
