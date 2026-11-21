using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Controllers;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.Application.Files;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Files;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Work;

/// <summary>
/// Visibilidad por archivo para la biblioteca: solo staff de la oficina, con cualquiera de los dos permisos de lectura;
/// cada parte se consulta solo con su permiso (y los formularios, con el módulo) y la petición se valida.
/// </summary>
public sealed class TaskFilesControllerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void Only_office_staff_with_either_read_permission_and_its_own_rate_limit()
    {
        var method = typeof(TaskFilesController).GetMethod(nameof(TaskFilesController.Visibility))!;

        Assert.Equal(
            [ActorType.TenantEmployee, ActorType.TenantAdmin],
            typeof(TaskFilesController).GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );
        Assert.Equal(
            [TasksPermissions.Read, TasksPermissions.FormRequestsRead],
            method.GetCustomAttribute<HasAnyPermissionAttribute>()!.Permissions
        );
        Assert.Equal("task.f.file_visibility", method.GetCustomAttribute<RateLimitAttribute>()!.PolicyName);
        Assert.Equal("tasks/files", typeof(TaskFilesController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("visibility", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public async Task Both_parts_are_asked_with_the_token_identity_and_merged()
    {
        var (controller, bus) = Controller(
            true,
            TasksPermissions.Read,
            TasksPermissions.FormRequestsRead,
            CustomersPermissions.ViewAll
        );
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var result = await controller.Visibility(new FileVisibilityRequest(ids, "download"), default);

        var response = Assert.IsType<FileVisibilityResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(
            [WorkResourceTypes.Task, WorkResourceTypes.FormRequest],
            response.Items.Select(i => i.ResourceType)
        );
        var tasks = Assert.Single(bus.Invoked.OfType<GetVisibleTaskFilesQuery>());
        Assert.Equal((Tenant, Actor, true), (tasks.TenantId, tasks.ActorUserId, tasks.CanViewAll));
        Assert.Equal(ids, tasks.FileIds);
        var forms = Assert.Single(bus.Invoked.OfType<GetVisibleFormFilesQuery>());
        Assert.Equal(
            (Tenant, Actor, true, "download"),
            (forms.TenantId, forms.ActorUserId, forms.CanViewAll, forms.Purpose)
        );
        Assert.Equal(ids, forms.FileIds);
    }

    [Fact]
    public async Task Without_tasks_read_tasks_are_not_queried()
    {
        var (controller, bus) = Controller(true, TasksPermissions.FormRequestsRead);

        await controller.Visibility(new FileVisibilityRequest([Guid.NewGuid()]), default);

        Assert.Empty(bus.Invoked.OfType<GetVisibleTaskFilesQuery>());
        Assert.Equal("list", Assert.Single(bus.Invoked.OfType<GetVisibleFormFilesQuery>()).Purpose);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Forms_need_their_permission_and_the_module(bool canReadForms, bool moduleOn)
    {
        var granted = canReadForms
            ? new[] { TasksPermissions.Read, TasksPermissions.FormRequestsRead }
            : [TasksPermissions.Read];
        var (controller, bus) = Controller(moduleOn, granted);

        await controller.Visibility(new FileVisibilityRequest([Guid.NewGuid()]), default);

        Assert.Empty(bus.Invoked.OfType<GetVisibleFormFilesQuery>());
        Assert.Single(bus.Invoked.OfType<GetVisibleTaskFilesQuery>());
    }

    public static TheoryData<FileVisibilityRequest?> InvalidRequests =>
        [
            null,
            new FileVisibilityRequest(null!),
            new FileVisibilityRequest([]),
            new FileVisibilityRequest(
                Enumerable.Range(0, FileVisibilityLimits.Batch + 1).Select(_ => Guid.NewGuid()).ToList()
            ),
            new FileVisibilityRequest([Guid.NewGuid()], "preview"),
        ];

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task An_invalid_request_is_a_bad_request(FileVisibilityRequest? request)
    {
        var (controller, bus) = Controller(true, TasksPermissions.Read, TasksPermissions.FormRequestsRead);

        var result = await controller.Visibility(request, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(
            "Task.Files.InvalidVisibilityRequest",
            Assert.IsType<BuildingBlocks.Results.Error>(problem.Value).Code
        );
        Assert.Empty(bus.Invoked);
    }

    private static (TaskFilesController Controller, RecordingBus Bus) Controller(bool formsOn, params string[] granted)
    {
        var bus = new RecordingBus();
        var controller = new TaskFilesController(bus.Fake, new GrantedPermissions(granted), new FormsModule(formsOn))
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
                    GetVisibleTaskFilesQuery q => (IReadOnlyList<VisibleFile>)
                        [new VisibleFile(q.FileIds[0], WorkResourceTypes.Task, Guid.NewGuid(), "task", null)],
                    GetVisibleFormFilesQuery q => (IReadOnlyList<VisibleFile>)
                        [new VisibleFile(q.FileIds[^1], WorkResourceTypes.FormRequest, Guid.NewGuid(), "form", null)],
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
