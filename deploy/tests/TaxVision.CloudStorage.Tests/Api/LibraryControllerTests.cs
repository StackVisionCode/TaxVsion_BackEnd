using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Api.Controllers;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Application.Library.Queries;
using TaxVision.CloudStorage.Tests.Application.Library;
using Wolverine;

namespace TaxVision.CloudStorage.Tests.Api;

public sealed class LibraryControllerTests
{
    private static readonly Type Controller = typeof(LibraryController);

    [Fact]
    public void Only_office_staff_reach_the_library()
    {
        var allowed = Controller.GetCustomAttribute<AllowActorTypesAttribute>();

        Assert.NotNull(allowed);
        Assert.Equal([ActorType.TenantEmployee, ActorType.TenantAdmin], allowed.ActorTypes.Order());
        Assert.NotNull(Controller.GetCustomAttribute<ExcludePlatformAdminAttribute>());
        Assert.Equal("storage/library", Controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.All(Actions(), action => Assert.Null(action.GetCustomAttribute<AllowActorTypesAttribute>()));
    }

    [Theory]
    [InlineData(nameof(LibraryController.Sources), "sources", "cloudstorage.f.library_sources")]
    [InlineData(nameof(LibraryController.Search), null, "cloudstorage.h.library_search")]
    [InlineData(nameof(LibraryController.SearchDocuments), "documents", "cloudstorage.h.library_search")]
    [InlineData(
        nameof(LibraryController.IssueDownloadUrl),
        "files/{fileId:guid}/download-url",
        "cloudstorage.f.library_download"
    )]
    public void Each_action_has_its_route_and_rate_limit(string name, string? template, string policy)
    {
        var action = Controller.GetMethod(name)!;

        Assert.Equal(template, action.GetCustomAttributes().OfType<HttpMethodAttribute>().Single().Template);
        Assert.Equal(policy, action.GetCustomAttribute<RateLimitAttribute>()!.PolicyName);
    }

    [Theory]
    [InlineData(nameof(LibraryController.Sources), new[] { CloudStoragePermissions.LibraryView })]
    [InlineData(nameof(LibraryController.Search), new[] { CloudStoragePermissions.LibraryView })]
    [InlineData(nameof(LibraryController.SearchDocuments), new[] { CloudStoragePermissions.FileView })]
    [InlineData(
        nameof(LibraryController.IssueDownloadUrl),
        new[] { CloudStoragePermissions.LibraryView, CloudStoragePermissions.FileDownload }
    )]
    public void Each_action_demands_its_permissions(string name, string[] permissions)
    {
        var policies = Controller
            .GetMethod(name)!
            .GetCustomAttributes<HasPermissionAttribute>()
            .Select(attribute => attribute.Policy)
            .Order();

        Assert.Equal(permissions.Select(p => HasPermissionAttribute.PolicyPrefix + p).Order(), policies);
        Assert.Null(Controller.GetCustomAttribute<HasPermissionAttribute>());
    }

    [Fact]
    public async Task An_invalid_query_is_a_400_with_the_library_code()
    {
        var (controller, bus) = NewController();

        var result = await controller.Search(new LibrarySearchRequest { Take = 500 }, default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(LibraryErrors.InvalidQueryCode, Assert.IsType<Error>(badRequest.Value).Code);
        Assert.Null(bus.Message);
    }

    [Fact]
    public async Task The_documents_search_forces_the_documents_source()
    {
        var (controller, bus) = NewController();

        await Assert.ThrowsAsync<FilesControllerScopeTests.StopException>(() =>
            controller.SearchDocuments(new LibrarySearchRequest { Source = ["billing"] }, default)
        );

        var query = Assert.IsType<SearchLibraryQuery>(bus.Message);
        Assert.Equal([LibrarySource.Documents], query.Filter.Sources);
    }

    [Fact]
    public async Task The_search_sends_the_resolved_access()
    {
        var (controller, bus) = NewController();

        await Assert.ThrowsAsync<FilesControllerScopeTests.StopException>(() =>
            controller.Search(new LibrarySearchRequest(), default)
        );

        var query = Assert.IsType<SearchLibraryQuery>(bus.Message);
        Assert.Same(FixedResolver.Access, query.Access);
    }

    [Fact]
    public async Task The_download_url_sends_the_resolved_access_and_scope()
    {
        var (controller, bus) = NewController();

        await Assert.ThrowsAsync<FilesControllerScopeTests.StopException>(() =>
            controller.IssueDownloadUrl(Guid.NewGuid(), default)
        );

        var query = Assert.IsType<IssueLibraryDownloadUrlQuery>(bus.Message);
        Assert.Same(FixedResolver.Access, query.Access);
        Assert.False(query.Scope.BypassAssignmentFilter);
    }

    [Fact]
    public void Platform_admin_is_turned_away_by_the_filter()
    {
        var context = new AuthorizationFilterContext(
            new ActionContext(
                new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [new Claim("actor_type", "PlatformAdmin"), new Claim(ClaimTypes.Role, "PlatformAdmin")],
                            "test"
                        )
                    ),
                },
                new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()
            ),
            []
        );

        new ExcludePlatformAdminAttribute().OnAuthorization(context);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    private static IEnumerable<MethodInfo> Actions() =>
        Controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes().OfType<HttpMethodAttribute>().Any());

    private static (LibraryController Controller, FilesControllerScopeTests.CapturingBus Bus) NewController()
    {
        var bus = DispatchProxy.Create<IMessageBus, FilesControllerScopeTests.CapturingBus>();
        var correlation = DispatchProxy.Create<ICorrelationContext, FilesControllerScopeTests.EmptyProxy>();
        var controller = new LibraryController(
            bus,
            correlation,
            new FixedResolver(),
            new FilesControllerScopeTests.MarkingResolver()
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = FilesControllerScopeTests.StaffUser() },
            },
        };
        return (controller, (FilesControllerScopeTests.CapturingBus)(object)bus);
    }

    private sealed class FixedResolver : ILibraryAccessResolver
    {
        public static readonly LibraryAccess Access = LibraryAccessPolicyTests.Access();

        public Task<LibraryAccess> ResolveAsync(
            ClaimsPrincipal user,
            Guid tenantId,
            Guid actorId,
            CancellationToken ct
        ) => Task.FromResult(Access);
    }
}
