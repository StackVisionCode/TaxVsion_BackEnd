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
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.Tests.ClientForms;

// Lecturas que necesita "Send to external email" y los intake links: avisos activos y plantillas asignables.
public sealed class ExternalSendReadAccessTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(TasksPermissions.FormConsentsManage)]
    [InlineData(TasksPermissions.FormTemplatesManage)]
    [InlineData(TasksPermissions.FormRequestsSendExternal)]
    [InlineData(TasksPermissions.FormIntakeLinksManage)]
    public async Task Active_notices_are_readable_with_each_permission(string permission)
    {
        var context = Context(permission);

        await Any<ConsentNoticesController>(nameof(ConsentNoticesController.ListActive)).OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    [Theory]
    [InlineData(TasksPermissions.FormRequestsManage)]
    [InlineData(TasksPermissions.FormRequestsSendExternal)]
    [InlineData(TasksPermissions.FormIntakeLinksManage)]
    public async Task Assignable_templates_are_readable_with_each_permission(string permission)
    {
        var context = Context(permission);

        await Any<FormTemplatesController>(nameof(FormTemplatesController.ListAssignable))
            .OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    [Theory]
    [InlineData(nameof(ConsentNoticesController.ListActive), typeof(ConsentNoticesController))]
    [InlineData(nameof(FormTemplatesController.ListAssignable), typeof(FormTemplatesController))]
    public async Task Without_any_of_the_permissions_the_read_is_forbidden(string action, Type controller)
    {
        var context = Context(TasksPermissions.FormRequestsRead);
        var method = controller.GetMethod(action)!;

        await method.GetCustomAttribute<HasAnyPermissionAttribute>()!.OnAuthorizationAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(context.Result).StatusCode);
        Assert.Null(method.GetCustomAttribute<HasPermissionAttribute>());
        Assert.Contains(ActorType.TenantEmployee, ActorTypes(controller, method));
    }

    [Fact]
    public void The_full_notice_list_stays_with_builders_and_consent_managers()
    {
        var permissions = typeof(ConsentNoticesController)
            .GetMethod(nameof(ConsentNoticesController.List))!
            .GetCustomAttribute<HasAnyPermissionAttribute>()!
            .Permissions;

        Assert.DoesNotContain(TasksPermissions.FormRequestsSendExternal, permissions);
        Assert.DoesNotContain(TasksPermissions.FormIntakeLinksManage, permissions);
    }

    [Fact]
    public async Task Active_list_only_returns_published_active_notices_with_their_latest_version()
    {
        var identity = Notice("identity_check", ConsentNoticePurpose.IdentityVerification);
        Publish(identity, "Old text");
        Publish(identity, "Current text");
        var draft = Notice("identity_draft", ConsentNoticePurpose.IdentityVerification);
        var archived = Notice("identity_old", ConsentNoticePurpose.IdentityVerification);
        Publish(archived, "Archived text");
        Assert.True(archived.Archive(UserId, Now).IsSuccess);
        var general = Notice("privacy", ConsentNoticePurpose.General);
        Publish(general, "Privacy");
        var repository = new MemoryConsentNoticeRepository(identity, draft, archived, general);

        var filtered = await ListActiveConsentNoticesHandler.Handle(
            new ListActiveConsentNoticesQuery(TenantId, ConsentNoticePurpose.IdentityVerification),
            repository,
            default
        );
        var all = await ListActiveConsentNoticesHandler.Handle(
            new ListActiveConsentNoticesQuery(TenantId, null),
            repository,
            default
        );

        var notice = Assert.Single(filtered);
        Assert.Equal(identity.Id, notice.Id);
        Assert.Equal(2, notice.VersionNumber);
        Assert.Equal("Current text", notice.TextSnapshot);
        Assert.Equal(
            new[] { general.Id, identity.Id }.Order().ToArray(),
            all.Select(item => item.Id).Order().ToArray()
        );
    }

    private static ConsentNotice Notice(string key, ConsentNoticePurpose purpose) =>
        ConsentNotice.Create(TenantId, key, key, purpose, UserId, Now).Value;

    private static void Publish(ConsentNotice notice, string text) =>
        Assert.True(
            notice
                .Publish("en", $"{{\"type\":\"doc\",\"text\":\"{text}\"}}", text, new string('a', 64), UserId, Now)
                .IsSuccess
        );

    private static HasAnyPermissionAttribute Any<T>(string action) =>
        typeof(T).GetMethod(action)!.GetCustomAttribute<HasAnyPermissionAttribute>()!;

    private static IEnumerable<ActorType> ActorTypes(Type controller, MethodInfo method) =>
        (
            method.GetCustomAttribute<AllowActorTypesAttribute>()
            ?? controller.GetCustomAttribute<AllowActorTypesAttribute>()!
        ).ActorTypes;

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
