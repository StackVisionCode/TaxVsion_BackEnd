using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Api.Controllers;

namespace TaxVision.PaymentApp.Tests.Api;

public sealed class PaymentAppAdminControllerTests
{
    [Fact]
    public void Refund_is_a_platform_admin_action_guarded_by_the_refund_permission()
    {
        var refund = typeof(PaymentAppAdminController).GetMethod(nameof(PaymentAppAdminController.Refund))!;

        var actors = typeof(PaymentAppAdminController).GetCustomAttribute<AllowActorTypesAttribute>()!;
        Assert.Equal(new[] { ActorType.PlatformAdmin }, actors.ActorTypes);
        Assert.Equal(
            HasPermissionAttribute.PolicyPrefix + PaymentAppPermissions.SaaSPaymentRefund,
            refund.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
        Assert.Equal("payments/{id:guid}/refund", refund.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void The_tenant_facing_saas_payments_controller_exposes_no_refund()
    {
        var actions = typeof(SaaSPaymentsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<HttpPostAttribute>() is not null);

        Assert.Empty(actions);
    }
}
