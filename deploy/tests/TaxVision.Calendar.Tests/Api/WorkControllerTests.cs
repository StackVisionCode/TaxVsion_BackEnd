using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.TenantTimeZones;
using BuildingBlocks.TimeZones;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
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

    [Fact]
    public void Titles_and_items_use_calendar_read_and_the_work_rate_limit()
    {
        Assert.Equal([$"perm:{CalendarPermissions.Read}"], Policies(nameof(WorkController.Titles)));
        Assert.Equal([$"perm:{CalendarPermissions.Read}"], Policies(nameof(WorkController.Items)));
        foreach (var action in new[] { nameof(WorkController.Titles), nameof(WorkController.Items) })
            Assert.Equal(
                "calendar.h.range",
                typeof(WorkController).GetMethod(action)!.GetCustomAttribute<RateLimitAttribute>()!.PolicyName
            );
        Assert.Equal(
            "titles",
            typeof(WorkController)
                .GetMethod(nameof(WorkController.Titles))!
                .GetCustomAttribute<HttpPostAttribute>()!
                .Template
        );
        Assert.Equal(
            "{userId:guid}/items",
            typeof(WorkController)
                .GetMethod(nameof(WorkController.Items))!
                .GetCustomAttribute<HttpGetAttribute>()!
                .Template
        );
    }

    [Fact]
    public async Task More_refs_than_the_title_batch_or_no_body_is_a_bad_request()
    {
        var (controller, bus) = Controller(CalendarPermissions.Read);
        var refs = Enumerable
            .Range(0, WorkLimits.TitleBatch + 1)
            .Select(_ => new WorkTitleRef(WorkResourceTypes.Appointment, Guid.NewGuid()))
            .ToList();

        var tooMany = await controller.Titles(new WorkTitlesRequest(refs), default);
        var noBody = await controller.Titles(null, default);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(tooMany).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(noBody).StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Titles_only_ask_for_appointments_and_ignore_other_types()
    {
        var (controller, bus) = Controller(CalendarPermissions.Read, CustomersPermissions.ViewAll);
        var appointment = Guid.NewGuid();

        var result = await controller.Titles(
            new WorkTitlesRequest([
                new WorkTitleRef(WorkResourceTypes.Task, Guid.NewGuid()),
                new WorkTitleRef(WorkResourceTypes.Appointment, appointment),
            ]),
            default
        );

        var response = Assert.IsType<WorkTitlesResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal([appointment], response.Items.Select(i => i.Id));
        var query = Assert.IsType<GetAppointmentWorkTitlesQuery>(Assert.Single(bus.Invoked));
        Assert.Equal((Tenant, Actor, true), (query.TenantId, query.ActorUserId, query.CanViewAll));
        Assert.Equal([appointment], query.Ids);
    }

    [Fact]
    public async Task Only_foreign_types_answer_empty_without_reading()
    {
        var (controller, bus) = Controller(CalendarPermissions.Read);

        var result = await controller.Titles(
            new WorkTitlesRequest([new WorkTitleRef(WorkResourceTypes.Note, Guid.NewGuid())]),
            default
        );

        Assert.Empty(Assert.IsType<WorkTitlesResponse>(Assert.IsType<OkObjectResult>(result).Value).Items);
        Assert.Empty(bus.Invoked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tasks")]
    [InlineData("Appointments")]
    public async Task Items_of_a_kind_calendar_does_not_serve_is_a_bad_request(string? kind)
    {
        var (controller, bus) = Controller(CalendarPermissions.Read);

        var result = await controller.Items(Actor, kind, null, null, default);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Someone_elses_items_without_both_rules_are_forbidden()
    {
        var (controller, bus) = Controller(UserManagementPermissions.UsersWorkView);

        Assert.IsType<ForbidResult>(await controller.Items(Ana, WorkKinds.Appointments, null, null, default));
        Assert.Empty(bus.Invoked);
    }

    [Theory]
    [InlineData(null, null, 1, WorkLimits.ItemsPageSize)]
    [InlineData(0, 900, 1, WorkLimits.ItemsMaxPageSize)]
    [InlineData(4, 10, 4, 10)]
    public async Task Items_page_with_the_viewer_rights(int? page, int? size, int expectedPage, int expectedSize)
    {
        var (controller, bus) = Controller(UserManagementPermissions.UsersWorkView, CustomersPermissions.ViewAll);

        var result = await controller.Items(Ana, WorkKinds.Appointments, page, size, default);

        Assert.IsType<PagedResult<WorkItem>>(Assert.IsType<OkObjectResult>(result).Value);
        var query = Assert.IsType<GetAppointmentWorkItemsQuery>(Assert.Single(bus.Invoked));
        Assert.Equal(
            (Tenant, Ana, Actor, true, expectedPage, expectedSize),
            (query.TenantId, query.UserId, query.ActorUserId, query.CanViewAll, query.Page, query.Size)
        );
        Assert.Equal(NewYork, query.OfficeZone.Id);
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
                return message switch
                {
                    GetAppointmentWorkQuery => Result.Success(new WorkSummaryResponse("calendar", "calendar", [], [])),
                    GetAppointmentWorkTitlesQuery q => (IReadOnlyList<WorkTitle>)
                        [.. q.Ids.Select(id => new WorkTitle(WorkResourceTypes.Appointment, id, "Review"))],
                    GetAppointmentWorkItemsQuery q => Result.Success(new PagedResult<WorkItem>([], q.Page, q.Size, 0)),
                    _ => Result.Success(new WorkBatchResponse([])),
                };
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
