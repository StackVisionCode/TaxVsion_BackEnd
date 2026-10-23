using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Tasks.Api.Controllers.ClientForms;
using TaxVision.Tasks.ClientForms.Application.Templates.Queries;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormTemplatesSecurityTests
{
    private sealed class DeniedPermissionsSource : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    [Fact]
    public void Controller_excludes_portal_actors()
    {
        var allowed = typeof(FormTemplatesController).GetCustomAttribute<AllowActorTypesAttribute>()!;

        Assert.Contains(ActorType.TenantEmployee, allowed.ActorTypes);
        Assert.Contains(ActorType.TenantAdmin, allowed.ActorTypes);
        Assert.Contains(ActorType.PlatformAdmin, allowed.ActorTypes);
        Assert.DoesNotContain(ActorType.CustomerPortal, allowed.ActorTypes);
    }

    [Fact]
    public void Mutations_require_manage_or_publish_and_draft_has_a_size_limit()
    {
        var update = typeof(FormTemplatesController).GetMethod(nameof(FormTemplatesController.UpdateDraft))!;
        var publish = typeof(FormTemplatesController).GetMethod(nameof(FormTemplatesController.Publish))!;

        Assert.Equal(
            "perm:" + TasksPermissions.FormTemplatesManage,
            update.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
        var sizeLimit = (IRequestSizeLimitMetadata)update.GetCustomAttribute<RequestSizeLimitAttribute>()!;
        Assert.Equal(600_000, sizeLimit.MaxRequestBodySize);
        Assert.Equal(
            "perm:" + TasksPermissions.FormTemplatesPublish,
            publish.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
    }

    [Fact]
    public async Task Template_from_another_tenant_is_not_visible()
    {
        var template = FormTemplateHandlerTestDoubles.Template(Guid.NewGuid());

        var result = await GetFormTemplateHandler.Handle(
            new GetFormTemplateQuery(FormTemplateHandlerTestDoubles.TenantId, template.Id),
            new MemoryFormTemplateRepository(template),
            new MemoryFormTemplateVersionRepository(),
            default
        );

        Assert.Equal(FormTemplateErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Employee_deny_on_publish_returns_permission_403()
    {
        var publish = typeof(FormTemplatesController).GetMethod(nameof(FormTemplatesController.Publish))!;
        var policyName = publish.GetCustomAttribute<HasPermissionAttribute>()!.Policy!;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AuthorizationMetrics>();
        services.AddSingleton<IUserPermissionsSource, DeniedPermissionsSource>();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        await using var provider = services.BuildServiceProvider();

        var identity = new ClaimsIdentity(
            [new Claim(ClaimNames.ActorType, ActorType.TenantEmployee.ToString())],
            "Test"
        );
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity), RequestServices = provider };
        context.Response.Body = new MemoryStream();
        var policy = await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(policyName);
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var decision = await authorization.AuthorizeAsync(context.User, context, policy!);
        Assert.False(decision.Succeeded);

        await new ProblemDetailsAuthorizationResultHandler().HandleAsync(
            _ => Task.CompletedTask,
            context,
            policy!,
            PolicyAuthorizationResult.Forbid()
        );

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal(AuthorizationDenial.PermissionDenied.Code, body.RootElement.GetProperty("code").GetString());
    }
}
