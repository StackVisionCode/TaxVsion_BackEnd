using BuildingBlocks.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Customer.Api.Controllers;
using TaxVision.Customer.Api.Requests;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.Queries.Search;
using TaxVision.Customer.Application.Customers.Queries.Work;

namespace TaxVision.Customer.Tests.Api;

/// <summary>
/// GET /customers?preparerUserId=&amp;assignedUserId= (y su gemelo POST /customers/search): filtrar por uno mismo
/// solo pide customers.view; filtrar por otra persona exige users.work.view. El ABAC del listado no cambia.
/// </summary>
public sealed class CustomerControllerStaffFilterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    [Fact]
    public async Task Search_by_own_preparer_id_forwards_the_filter_without_users_work_view()
    {
        var bus = new WorkRecordingBus();

        var response = await Controller(bus).Search(preparerUserId: ActorId, ct: CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
        var query = Assert.IsType<SearchCustomersQuery>(Assert.Single(bus.Invoked));
        Assert.Equal(new CustomerStaffFilter(ActorId, null), query.Staff);
        Assert.False(query.CanViewAll);
    }

    [Fact]
    public async Task Search_by_other_assigned_user_without_users_work_view_is_403()
    {
        var bus = new WorkRecordingBus();

        var response = await Controller(bus, CustomersPermissions.ViewAll)
            .Search(assignedUserId: OtherId, ct: CancellationToken.None);

        var denied = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(403, denied.StatusCode);
        Assert.Equal(CustomerWorkErrors.OtherUserForbidden, denied.Value);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Search_by_other_with_users_work_view_combines_both_filters_and_keeps_abac()
    {
        var bus = new WorkRecordingBus();

        var response = await Controller(bus, UserManagementPermissions.UsersWorkView)
            .Search(
                term: "ana",
                status: CustomerStatusFilter.All,
                preparerUserId: OtherId,
                assignedUserId: ActorId,
                ct: CancellationToken.None
            );

        Assert.IsType<OkObjectResult>(response.Result);
        var query = Assert.IsType<SearchCustomersQuery>(Assert.Single(bus.Invoked));
        Assert.Equal(new CustomerStaffFilter(OtherId, ActorId), query.Staff);
        Assert.Equal("ana", query.Term);
        Assert.Equal(CustomerStatusFilter.All, query.Status);
        Assert.Equal(ActorId, query.ActorUserId);
        Assert.False(query.CanViewAll);
    }

    [Fact]
    public async Task SearchByBody_by_other_preparer_without_users_work_view_is_403()
    {
        var bus = new WorkRecordingBus();

        var response = await Controller(bus)
            .SearchByBody(new SearchCustomersRequest(PreparerUserId: OtherId), CancellationToken.None);

        Assert.Equal(403, Assert.IsType<ObjectResult>(response.Result).StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Search_without_staff_filters_sends_no_filter()
    {
        var bus = new WorkRecordingBus();

        await Controller(bus).Search(ct: CancellationToken.None);

        Assert.Null(Assert.IsType<SearchCustomersQuery>(Assert.Single(bus.Invoked)).Staff);
    }

    private static CustomerController Controller(WorkRecordingBus bus, params string[] granted) =>
        new(bus, new GrantedPermissions(granted))
        {
            ControllerContext = CustomerWorkControllers.ContextFor(TenantId, ActorId),
        };
}
