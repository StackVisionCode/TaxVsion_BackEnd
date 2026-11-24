using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Controllers;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// Tablero (R4: cabecera de corte con el cuerpo intacto) y alcance del resumen: el servidor decide la oficina con
/// el permiso del token, no con lo que diga el CRM.
/// </summary>
public sealed class TasksControllerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task An_unknown_scope_is_a_bad_request()
    {
        var (controller, invoked) = Controller([TasksPermissions.Read], _ => Summary());

        var result = await controller.Summary(null, null, null, "everyone", default);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(invoked);
    }

    [Theory]
    [InlineData(new[] { TasksPermissions.Read }, false)]
    [InlineData(new[] { TasksPermissions.Read, UserManagementPermissions.UsersWorkView }, true)]
    public async Task Office_scope_carries_the_token_permission(string[] granted, bool canViewOffice)
    {
        var (controller, invoked) = Controller(granted, _ => Summary());

        await controller.Summary(null, null, null, "office", default);

        var query = Assert.IsType<GetTaskSummaryQuery>(Assert.Single(invoked));
        Assert.Equal((DashboardScope.Office, canViewOffice), (query.Scope, query.CanViewOffice));
    }

    [Fact]
    public async Task A_forbidden_office_answers_403()
    {
        var (controller, _) = Controller(
            [TasksPermissions.Read],
            _ => Result.Failure<TaskSummaryResponse>(DashboardScopes.OfficeForbidden)
        );

        var result = await controller.Summary(null, null, null, "office", default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    private static Result<TaskSummaryResponse> Summary() =>
        Result.Success(
            new TaskSummaryResponse(
                new DateOnly(2026, 11, 21),
                0,
                new TaskStatusCounts(0, 0, 0),
                0,
                0,
                0,
                0,
                0,
                null,
                [],
                [],
                false
            )
        );

    private static (TasksController Controller, List<object> Invoked) Controller(
        string[] granted,
        Func<object, object> reply
    )
    {
        var invoked = new List<object>();
        var bus = new FakeMessageBus
        {
            OnInvokeReply = message =>
            {
                invoked.Add(message);
                return reply(message);
            },
        };
        var controller = new TasksController(bus, new GrantedPermissions(granted))
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
        return (controller, invoked);
    }

    private sealed class GrantedPermissions(string[] granted) : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(granted.Contains(permission));
    }
}
