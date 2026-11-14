using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.Subscription.Api.Controllers.Internal;
using TaxVision.Subscription.Application.Entitlements.Queries;
using Wolverine;

namespace TaxVision.Subscription.Tests.Api;

// Resumen de entitlements por M2M: lo usan los servicios para reparar su proyección local del plan.
public sealed class InternalTenantEntitlementsControllerTests
{
    [Fact]
    public void Endpoint_is_service_only_internal_and_exempt_from_rate_limits()
    {
        var controller = typeof(InternalTenantEntitlementsController);
        var action = controller.GetMethod(nameof(InternalTenantEntitlementsController.GetSummary))!;

        Assert.Equal("internal/tenants", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("{tenantId:guid}/entitlements", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("ServiceOnly", controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal([ActorType.Service], controller.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes);
        Assert.NotNull(action.GetCustomAttribute<RateLimitExemptAttribute>());
        Assert.Empty(action.GetCustomAttributes<HasPermissionAttribute>());
    }

    [Fact]
    public async Task Returns_the_summary_of_the_requested_tenant()
    {
        var tenant = Guid.NewGuid();
        var summary = new EntitlementSummaryResponse(
            tenant,
            4,
            DateTime.UtcNow,
            "enterprise",
            "Active",
            1,
            1,
            new Dictionary<string, EntitlementValueResponse>
            {
                ["financial.documents_per_month"] = new("Int", "200", "Active", "Plan", null),
            }
        );
        var bus = new TestMessageContext();
        bus.WhenInvokedMessageOf<GetTenantEntitlementSummaryQuery>(q => q.TenantId == tenant)
            .RespondWith(Result.Success(summary));

        var result = await new InternalTenantEntitlementsController(bus).GetSummary(tenant, CancellationToken.None);

        Assert.Same(summary, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Missing_snapshot_keeps_the_query_error()
    {
        var bus = new TestMessageContext();
        bus.WhenInvokedMessageOf<GetTenantEntitlementSummaryQuery>()
            .RespondWith(Result.Failure<EntitlementSummaryResponse>(new Error("EntitlementSnapshot.NotFound", "x")));

        var result = await new InternalTenantEntitlementsController(bus).GetSummary(Guid.NewGuid(), CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal("EntitlementSnapshot.NotFound", Assert.IsType<Error>(error.Value).Code);
    }
}
