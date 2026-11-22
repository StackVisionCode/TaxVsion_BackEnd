using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Controllers;
using TaxVision.Tasks.Application.Work;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Work;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Work;

/// <summary>
/// Títulos en vivo y lista paginada de Tasks: staff con tasks.read, tenant del token, el lote acotado, los tipos
/// ajenos ignorados y los form requests solo con su permiso y el módulo disponible.
/// </summary>
public sealed class WorkControllerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();

    [Fact]
    public void Titles_and_items_use_the_read_permission_and_the_work_rate_limit()
    {
        foreach (var action in new[] { nameof(WorkController.Titles), nameof(WorkController.Items) })
        {
            var method = typeof(WorkController).GetMethod(action)!;
            Assert.Equal(
                [$"perm:{TasksPermissions.Read}"],
                method.GetCustomAttributes<HasPermissionAttribute>().Select(a => a.Policy)
            );
            Assert.Equal("task.f.read", method.GetCustomAttribute<RateLimitAttribute>()!.PolicyName);
        }
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
    public async Task More_refs_than_the_title_batch_is_a_bad_request()
    {
        var (controller, bus) = Controller(TasksPermissions.Read);
        var refs = Enumerable
            .Range(0, WorkLimits.TitleBatch + 1)
            .Select(_ => new WorkTitleRef(WorkResourceTypes.Task, Guid.NewGuid()))
            .ToList();

        var result = await controller.Titles(new WorkTitlesRequest(refs), default);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task A_missing_body_or_list_is_a_bad_request()
    {
        var (controller, bus) = Controller(TasksPermissions.Read);

        var noBody = await controller.Titles(null, default);
        var noList = await controller.Titles(new WorkTitlesRequest(null!), default);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(noBody).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(noList).StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Each_part_is_asked_only_for_its_types_and_foreign_types_are_ignored()
    {
        var (controller, bus) = Controller(TasksPermissions.Read, TasksPermissions.FormRequestsRead);
        Guid task = Guid.NewGuid(),
            request = Guid.NewGuid(),
            form = Guid.NewGuid();

        var result = await controller.Titles(
            new WorkTitlesRequest([
                new WorkTitleRef(WorkResourceTypes.Task, task),
                new WorkTitleRef(WorkResourceTypes.ClientRequest, request),
                new WorkTitleRef(WorkResourceTypes.FormRequest, form),
                new WorkTitleRef(WorkResourceTypes.Appointment, Guid.NewGuid()),
                new WorkTitleRef("unknown", Guid.NewGuid()),
            ]),
            default
        );

        var response = Assert.IsType<WorkTitlesResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(["task title", "form title"], response.Items.Select(i => i.Title));
        var tasks = Assert.Single(bus.Invoked.OfType<GetWorkTitlesQuery>());
        Assert.Equal((Tenant, Actor), (tasks.TenantId, tasks.ActorUserId));
        Assert.Equal([task], tasks.TaskIds);
        Assert.Equal([request], tasks.ClientRequestIds);
        var forms = Assert.Single(bus.Invoked.OfType<GetFormRequestWorkTitlesQuery>());
        Assert.Equal((Tenant, Actor), (forms.TenantId, forms.ActorUserId));
        Assert.Equal([form], forms.Ids);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Form_titles_need_the_form_permission_and_the_module(bool canReadForms, bool moduleOn)
    {
        var granted = canReadForms
            ? new[] { TasksPermissions.Read, TasksPermissions.FormRequestsRead }
            : [TasksPermissions.Read];
        var (controller, bus) = Controller(moduleOn, granted);

        var result = await controller.Titles(
            new WorkTitlesRequest([new WorkTitleRef(WorkResourceTypes.FormRequest, Guid.NewGuid())]),
            default
        );

        var response = Assert.IsType<WorkTitlesResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(response.Items);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Only_foreign_types_answer_empty_without_reading()
    {
        var (controller, bus) = Controller(TasksPermissions.Read, TasksPermissions.FormRequestsRead);

        var result = await controller.Titles(
            new WorkTitlesRequest([new WorkTitleRef(WorkResourceTypes.Appointment, Guid.NewGuid())]),
            default
        );

        Assert.Empty(Assert.IsType<WorkTitlesResponse>(Assert.IsType<OkObjectResult>(result).Value).Items);
        Assert.Empty(bus.Invoked);
    }

    [Theory]
    [InlineData(null, null, 1, WorkLimits.ItemsPageSize)]
    [InlineData(0, 0, 1, WorkLimits.ItemsPageSize)]
    [InlineData(3, 500, 3, WorkLimits.ItemsMaxPageSize)]
    [InlineData(2, 7, 2, 7)]
    public async Task Items_normalize_the_page(int? page, int? size, int expectedPage, int expectedSize)
    {
        var (controller, bus) = Controller(TasksPermissions.Read, UserManagementPermissions.UsersWorkView);

        var result = await controller.Items(Ana, WorkKinds.Tasks, page, size, default);

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<GetWorkItemsQuery>(Assert.Single(bus.Invoked));
        Assert.Equal(
            (Tenant, Actor, Ana, true, WorkKinds.Tasks, expectedPage, expectedSize),
            (query.TenantId, query.ActorUserId, query.UserId, query.CanViewOthers, query.Kind, query.Page, query.Size)
        );
    }

    [Fact]
    public async Task Someone_elses_form_requests_without_users_work_view_are_forbidden()
    {
        var (controller, bus) = Controller(TasksPermissions.Read, TasksPermissions.FormRequestsRead);

        var result = await controller.Items(Ana, WorkKinds.FormRequests, null, null, default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Form_request_items_need_the_form_permission()
    {
        var (controller, bus) = Controller(TasksPermissions.Read);

        var result = await controller.Items(Actor, WorkKinds.FormRequests, null, null, default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Own_form_request_items_page_with_the_viewer_inbox()
    {
        var (controller, bus) = Controller(
            TasksPermissions.Read,
            TasksPermissions.FormRequestsRead,
            CustomersPermissions.ViewAll
        );

        var result = await controller.Items(Actor, WorkKinds.FormRequests, 2, 5, default);

        Assert.IsType<PagedResult<WorkItem>>(Assert.IsType<OkObjectResult>(result).Value);
        var query = Assert.IsType<GetFormRequestWorkItemsQuery>(Assert.Single(bus.Invoked));
        Assert.Equal(
            (Tenant, Actor, Actor, true, 2, 5),
            (query.TenantId, query.ActorUserId, query.UserId, query.CanViewAll, query.Page, query.Size)
        );
    }

    [Fact]
    public async Task Office_waiting_without_users_work_view_is_forbidden()
    {
        var (controller, bus) = Controller(TasksPermissions.Read, TasksPermissions.FormRequestsRead);

        var result = await controller.OfficeWaiting(default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        var query = Assert.IsType<GetOfficeWaitingQuery>(Assert.Single(bus.Invoked));
        Assert.False(query.CanViewOffice);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task Office_waiting_adds_forms_only_with_their_permission_and_module(
        bool canReadForms,
        bool moduleOn,
        bool withForms
    )
    {
        var granted = new List<string> { TasksPermissions.Read, UserManagementPermissions.UsersWorkView };
        if (canReadForms)
            granted.Add(TasksPermissions.FormRequestsRead);
        var (controller, bus) = Controller(moduleOn, [.. granted]);

        var result = await controller.OfficeWaiting(default);

        var response = Assert.IsType<OfficeWaitingResponse>(Assert.IsType<OkObjectResult>(result).Value);
        var kinds = response.Counts.Select(c => c.Kind).ToList();
        Assert.Equal(withForms, kinds.Contains(WorkKinds.FormRequests));
        var tasks = Assert.Single(bus.Invoked.OfType<GetOfficeWaitingQuery>());
        Assert.Equal(
            (Tenant, Actor, true, false),
            (tasks.TenantId, tasks.ActorUserId, tasks.CanViewOffice, tasks.CanViewAll)
        );
        Assert.Equal(withForms ? 1 : 0, bus.Invoked.OfType<GetFormRequestOfficeWaitingQuery>().Count());
    }

    [Fact]
    public void Office_waiting_uses_the_read_permission_and_the_work_rate_limit()
    {
        var method = typeof(WorkController).GetMethod(nameof(WorkController.OfficeWaiting))!;
        Assert.Equal(
            [$"perm:{TasksPermissions.Read}"],
            method.GetCustomAttributes<HasPermissionAttribute>().Select(a => a.Policy)
        );
        Assert.Equal("task.f.read", method.GetCustomAttribute<RateLimitAttribute>()!.PolicyName);
        Assert.Equal("office/waiting", method.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    private static (WorkController Controller, RecordingBus Bus) Controller(params string[] granted) =>
        Controller(true, granted);

    private static (WorkController Controller, RecordingBus Bus) Controller(bool formsOn, params string[] granted)
    {
        var bus = new RecordingBus();
        var controller = new WorkController(bus.Fake, new GrantedPermissions(granted), new FormsModule(formsOn))
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
                    GetWorkTitlesQuery q => (IReadOnlyList<WorkTitle>)
                        [new WorkTitle(WorkResourceTypes.Task, q.TaskIds[0], "task title")],
                    GetFormRequestWorkTitlesQuery q => (IReadOnlyList<WorkTitle>)
                        [new WorkTitle(WorkResourceTypes.FormRequest, q.Ids[0], "form title")],
                    GetWorkItemsQuery q => Result.Success(new PagedResult<WorkItem>([], q.Page, q.Size, 0)),
                    GetFormRequestWorkItemsQuery q => new PagedResult<WorkItem>([], q.Page, q.Size, 0),
                    GetOfficeWaitingQuery q => q.CanViewOffice
                        ? Result.Success<IReadOnlyList<OfficeWaitingCount>>([
                            new OfficeWaitingCount(WorkKinds.Tasks, 2),
                            new OfficeWaitingCount(WorkKinds.ClientRequests, 1),
                        ])
                        : Result.Failure<IReadOnlyList<OfficeWaitingCount>>(WorkErrors.Forbidden),
                    GetFormRequestOfficeWaitingQuery => 4,
                    _ => throw new InvalidOperationException(message.GetType().Name),
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

    private sealed class FormsModule(bool enabled) : IClientFormsAvailabilityEvaluator
    {
        public Task<ClientFormsAvailability> EvaluateAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(enabled ? ClientFormsAvailability.Available : new ClientFormsAvailability(false, "off"));
    }
}
