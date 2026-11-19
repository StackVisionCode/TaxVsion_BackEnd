using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Customer.Api.Controllers;
using TaxVision.Customer.Application.Customers.Queries.Work;

namespace TaxVision.Customer.Tests.Api;

/// <summary>
/// GET /customers/work/{userId} y /customers/work/summary: lo propio con customers.view; otra persona y el lote
/// exigen además users.work.view. El tenant sale siempre del JWT.
/// </summary>
public sealed class CustomerWorkControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    [Fact]
    public async Task ForUser_own_work_needs_no_users_work_view()
    {
        var bus = new WorkRecordingBus();

        var response = await Controller(bus).ForUser(ActorId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        Assert.Equal(
            new GetCustomerWorkQuery(TenantId, ActorId, ActorId, CanViewAll: false),
            Assert.Single(bus.Invoked)
        );
    }

    [Fact]
    public async Task ForUser_other_member_without_users_work_view_is_403_without_querying()
    {
        var bus = new WorkRecordingBus();

        var response = await Controller(bus, CustomersPermissions.ViewAll).ForUser(OtherId, CancellationToken.None);

        var denied = Assert.IsType<ObjectResult>(response);
        Assert.Equal(403, denied.StatusCode);
        Assert.Equal(CustomerWorkErrors.OtherUserForbidden, denied.Value);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task ForUser_other_member_with_users_work_view_keeps_the_viewer_abac()
    {
        var bus = new WorkRecordingBus();

        var response = await Controller(bus, UserManagementPermissions.UsersWorkView)
            .ForUser(OtherId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        Assert.Equal(
            new GetCustomerWorkQuery(TenantId, OtherId, ActorId, CanViewAll: false),
            Assert.Single(bus.Invoked)
        );
    }

    [Fact]
    public async Task Summary_without_users_work_view_is_403_even_for_oneself()
    {
        var bus = new WorkRecordingBus();

        var response = await Controller(bus, CustomersPermissions.ViewAll)
            .Summary(ActorId.ToString(), CancellationToken.None);

        Assert.Equal(403, Assert.IsType<ObjectResult>(response).StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Summary_with_users_work_view_sends_the_parsed_ids_and_view_all()
    {
        var bus = new WorkRecordingBus();

        var response = await Controller(bus, UserManagementPermissions.UsersWorkView, CustomersPermissions.ViewAll)
            .Summary($"{ActorId}, {OtherId},{ActorId}", CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        var query = Assert.IsType<GetCustomerWorkBatchQuery>(Assert.Single(bus.Invoked));
        Assert.Equal(TenantId, query.TenantId);
        Assert.Equal([ActorId, OtherId], query.UserIds);
        Assert.True(query.CanViewAll);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task Summary_with_invalid_ids_is_400(string? raw)
    {
        var bus = new WorkRecordingBus();

        var response = await Controller(bus, UserManagementPermissions.UsersWorkView)
            .Summary(raw, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(CustomerWorkErrors.InvalidUserIds, bad.Value);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Summary_with_more_than_100_ids_is_400()
    {
        var bus = new WorkRecordingBus();
        var raw = string.Join(',', Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()));

        var response = await Controller(bus, UserManagementPermissions.UsersWorkView)
            .Summary(raw, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response);
    }

    [Fact]
    public void Endpoints_declare_route_actor_types_permission_and_rate_limit()
    {
        var type = typeof(CustomerWorkController);
        Assert.Equal("customers/work", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(
            [ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin],
            type.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );
        foreach (var name in new[] { nameof(CustomerWorkController.ForUser), nameof(CustomerWorkController.Summary) })
        {
            var action = type.GetMethod(name)!;
            Assert.Equal(
                HasPermissionAttribute.PolicyPrefix + CustomersPermissions.View,
                action.GetCustomAttribute<HasPermissionAttribute>()!.Policy
            );
            Assert.NotNull(action.GetCustomAttribute<BuildingBlocks.Web.RateLimiting.RateLimitAttribute>());
        }
        Assert.Equal("{userId:guid}", type.GetMethod("ForUser")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("summary", type.GetMethod("Summary")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    private static CustomerWorkController Controller(WorkRecordingBus bus, params string[] granted) =>
        new(bus, new GrantedPermissions(granted))
        {
            ControllerContext = CustomerWorkControllers.ContextFor(TenantId, ActorId),
        };
}
