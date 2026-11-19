using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.TenantTimeZones;
using BuildingBlocks.TimeZones;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Calendar.Api.Controllers;
using TaxVision.Calendar.Application.Appointments.Work;
using TaxVision.Calendar.Tests.Application;
using Xunit;

namespace TaxVision.Calendar.Tests.Api;

/// <summary>
/// Work Profile (Calendar): la propia agenda con calendar.read; la de otro exige users.work.view y además poder
/// ver agendas ajenas (calendar.manage_all o customers.view_all); el lote, lo mismo. El tenant sale del JWT.
/// </summary>
public sealed class WorkControllerTests
{
    private const string NewYork = "America/New_York";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();

    [Fact]
    public void Staff_only_with_calendar_read_and_the_batch_with_users_work_view()
    {
        var actors = typeof(WorkController).GetCustomAttribute<AllowActorTypesAttribute>();
        Assert.NotNull(actors);
        Assert.Equal(
            [ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin],
            actors.ActorTypes.OrderBy(a => a)
        );
        Assert.Equal([$"perm:{CalendarPermissions.Read}"], Policies(nameof(WorkController.ForUser)));
        Assert.Equal(
            [$"perm:{CalendarPermissions.Read}", $"perm:{UserManagementPermissions.UsersWorkView}"],
            Policies(nameof(WorkController.Summary))
        );
    }

    [Fact]
    public async Task Own_work_needs_no_extra_permission()
    {
        var (controller, bus) = Controller();

        var result = await controller.ForUser(Actor, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<WorkSummaryResponse>(ok.Value);
        var query = Assert.IsType<GetAppointmentWorkQuery>(Assert.Single(bus.Invoked));
        Assert.Equal(
            new
            {
                TenantId = Tenant,
                UserId = Actor,
                ActorUserId = Actor,
            },
            new
            {
                query.TenantId,
                query.UserId,
                query.ActorUserId,
            }
        );
        Assert.Equal(NewYork, query.OfficeZone.Id);
    }

    [Theory]
    [InlineData(CalendarPermissions.ManageAll, CustomersPermissions.ViewAll)]
    [InlineData(UserManagementPermissions.UsersWorkView, null)]
    public async Task Someone_elses_work_without_both_rules_is_forbidden(string first, string? second)
    {
        var (controller, bus) = Controller(second is null ? [first] : [first, second]);

        Assert.IsType<ForbidResult>(await controller.ForUser(Ana, default));
        Assert.IsType<ForbidResult>(await controller.Summary($"{Ana}", default));
        Assert.Empty(bus.Invoked);
    }

    [Theory]
    [InlineData(CalendarPermissions.ManageAll, false)]
    [InlineData(CustomersPermissions.ViewAll, true)]
    public async Task Someone_elses_work_with_users_work_view_and_the_agenda_rule(string agenda, bool canViewAll)
    {
        var (controller, bus) = Controller(UserManagementPermissions.UsersWorkView, agenda);

        Assert.IsType<OkObjectResult>(await controller.ForUser(Ana, default));
        Assert.IsType<OkObjectResult>(await controller.Summary($"{Ana},{Actor}", default));

        var single = Assert.IsType<GetAppointmentWorkQuery>(bus.Invoked[0]);
        Assert.Equal(Ana, single.UserId);
        Assert.Equal(canViewAll, single.CanViewAll);
        var batch = Assert.IsType<GetAppointmentWorkBatchQuery>(bus.Invoked[1]);
        Assert.Equal([Ana, Actor], batch.UserIds);
        Assert.Equal(Tenant, batch.TenantId);
        Assert.Equal(canViewAll, batch.CanViewAll);
    }

    [Fact]
    public async Task Batch_with_bad_user_ids_is_a_bad_request()
    {
        var (controller, bus) = Controller(UserManagementPermissions.UsersWorkView, CalendarPermissions.ManageAll);

        var result = await controller.Summary("nope", default);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(bus.Invoked);
    }

    private static string[] Policies(string action) =>
        [
            .. typeof(WorkController)
                .GetMethod(action)!
                .GetCustomAttributes<HasPermissionAttribute>()
                .Select(a => a.Policy!)
                .Order(),
        ];

    private static (WorkController Controller, RecordingBus Bus) Controller(params string[] granted)
    {
        var bus = new RecordingBus();
        var controller = new WorkController(bus.Fake, new GrantedPermissions(granted), new OfficeInNewYork())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [new Claim("sub", Actor.ToString()), new Claim(ClaimNames.TenantId, Tenant.ToString())],
                            "test"
                        )
                    ),
                },
            },
        };
        return (controller, bus);
    }

    private sealed class RecordingBus
    {
        public RecordingBus()
        {
            Fake.OnInvokeReply = message =>
            {
                Invoked.Add(message);
                return message is GetAppointmentWorkQuery
                    ? Result.Success(new WorkSummaryResponse("calendar", "calendar", [], []))
                    : Result.Success(new WorkBatchResponse([]));
            };
        }

        public FakeMessageBus Fake { get; } = new();
        public List<object> Invoked { get; } = [];
    }

    private sealed class GrantedPermissions(string[] granted) : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(granted.Contains(permission));
    }

    private sealed class OfficeInNewYork : ITenantTimeZoneResolver
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(tenantId == Tenant ? ResolvedTimeZone.Resolve(NewYork) : ResolvedTimeZone.Utc);

        public void Forget(Guid tenantId) { }
    }
}
