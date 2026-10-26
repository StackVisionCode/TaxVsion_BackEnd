using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Tasks.Api.Controllers.ClientForms;
using TaxVision.Tasks.Api.Filters;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ClientFormsStaffAccessTests
{
    [Fact]
    public void Availability_accepts_platform_admin_and_any_staff_permission()
    {
        var allowed = typeof(FormAvailabilityController).GetCustomAttribute<AllowActorTypesAttribute>()!;
        var any = Method<FormAvailabilityController>(nameof(FormAvailabilityController.Get))
            .GetCustomAttribute<HasAnyPermissionAttribute>()!;

        Assert.Contains(ActorType.PlatformAdmin, allowed.ActorTypes);
        Assert.Contains(TasksPermissions.FormRequestsRead, any.Permissions);
        Assert.Contains(TasksPermissions.FormTemplatesRead, any.Permissions);
        Assert.Null(
            Method<FormAvailabilityController>(nameof(FormAvailabilityController.Get))
                .GetCustomAttribute<HasPermissionAttribute>()
        );
    }

    [Theory]
    [InlineData(nameof(ConsentNoticesController.List))]
    [InlineData(nameof(ConsentNoticesController.Get))]
    public void Template_builders_can_read_consent_notices(string action)
    {
        var method = Method<ConsentNoticesController>(action);

        Assert.Contains(ActorType.TenantEmployee, method.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes);
        Assert.Contains(
            TasksPermissions.FormTemplatesManage,
            method.GetCustomAttribute<HasAnyPermissionAttribute>()!.Permissions
        );
        Assert.Null(typeof(ConsentNoticesController).GetCustomAttribute<HasPermissionAttribute>());
    }

    [Theory]
    [InlineData(nameof(ConsentNoticesController.Create))]
    [InlineData(nameof(ConsentNoticesController.Update))]
    [InlineData(nameof(ConsentNoticesController.Publish))]
    [InlineData(nameof(ConsentNoticesController.Archive))]
    public void Managing_consent_notices_stays_with_admins(string action)
    {
        var method = Method<ConsentNoticesController>(action);
        var allowed = typeof(ConsentNoticesController).GetCustomAttribute<AllowActorTypesAttribute>()!;

        Assert.Null(method.GetCustomAttribute<AllowActorTypesAttribute>());
        Assert.DoesNotContain(ActorType.TenantEmployee, allowed.ActorTypes);
        Assert.Equal(
            "perm:" + TasksPermissions.FormConsentsManage,
            method.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
    }

    [Theory]
    [InlineData(nameof(FormRequestsController.LinkCustomer), TasksPermissions.FormRequestsReview)]
    [InlineData(nameof(FormRequestsController.ReassignResponsible), TasksPermissions.FormRequestsManage)]
    [InlineData(nameof(FormRequestsController.Archive), TasksPermissions.FormRequestsManage)]
    public void Inbox_decisions_require_their_exact_permission(string action, string permission)
    {
        var method = Method<FormRequestsController>(action);

        Assert.Equal("perm:" + permission, method.GetCustomAttribute<HasPermissionAttribute>()!.Policy);
    }

    [Fact]
    public async Task Any_permission_passes_with_a_single_match()
    {
        var context = Context(TasksPermissions.FormRequestsRead);

        await new HasAnyPermissionAttribute(
            TasksPermissions.FormTemplatesRead,
            TasksPermissions.FormRequestsRead
        ).OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Any_permission_denies_with_a_permission_problem()
    {
        var context = Context();

        await new HasAnyPermissionAttribute(
            TasksPermissions.FormTemplatesRead,
            TasksPermissions.FormRequestsRead
        ).OnAuthorizationAsync(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(
            AuthorizationDenial.PermissionDenied.Code,
            AuthorizationDenial.Recorded(context.HttpContext)!.Code
        );
    }

    private static MethodInfo Method<T>(string name) => typeof(T).GetMethod(name)!;

    private static AuthorizationFilterContext Context(params string[] granted)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AuthorizationMetrics>();
        services.AddSingleton<IUserPermissionsSource>(new GrantedPermissions(granted));
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimNames.ActorType, ActorType.TenantEmployee.ToString())], "Test")
            ),
        };
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
    }

    private sealed class GrantedPermissions(string[] granted) : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(granted.Contains(permission));
    }
}
