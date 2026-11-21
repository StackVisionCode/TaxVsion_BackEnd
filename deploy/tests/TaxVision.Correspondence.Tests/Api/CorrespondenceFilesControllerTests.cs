using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Api.Controllers;
using TaxVision.Correspondence.Application.Files;

namespace TaxVision.Correspondence.Tests.Api;

// POST /correspondence/files/visibility: solo staff con correspondence.read, con su propia cuota.
public sealed class CorrespondenceFilesControllerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Viewer = Guid.NewGuid();

    private static readonly MethodInfo Action = typeof(CorrespondenceFilesController).GetMethod(
        nameof(CorrespondenceFilesController.Visibility)
    )!;

    private static (CorrespondenceFilesController Controller, ControllableMessageBus Bus) Controller(
        IReadOnlyCollection<Guid>? visible = null,
        Guid? restrictTo = null,
        bool canDownloadAttachments = true
    )
    {
        var bus = new ControllableMessageBus { NextResult = new FileVisibilityResponse([]) };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("tenant_id", Tenant.ToString()), new Claim("sub", Viewer.ToString())], "Test")
        );
        var controller = new CorrespondenceFilesController(
            bus,
            new FakeMailboxVisibilityResolver { Visible = visible },
            new FakeCustomerAccessResolver { RestrictTo = restrictTo },
            new GrantedPermissions(canDownloadAttachments ? [CorrespondencePermissions.AttachmentDownload] : [])
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
        return (controller, bus);
    }

    [Fact]
    public void Only_staff_actors_are_allowed()
    {
        var allowed = typeof(CorrespondenceFilesController).GetCustomAttribute<AllowActorTypesAttribute>()!;

        Assert.Equal([ActorType.TenantEmployee, ActorType.TenantAdmin], allowed.ActorTypes.Order());
    }

    [Fact]
    public void Requires_correspondence_read_and_declares_its_rate_limit()
    {
        var permission = Assert.Single(Action.GetCustomAttributes<HasPermissionAttribute>());
        Assert.Equal($"{HasPermissionAttribute.PolicyPrefix}{CorrespondencePermissions.Read}", permission.Policy);
        Assert.Equal("correspondence.f.file_visibility", Action.GetCustomAttribute<RateLimitAttribute>()!.PolicyName);
        Assert.Equal(
            "correspondence/files",
            typeof(CorrespondenceFilesController).GetCustomAttribute<RouteAttribute>()!.Template
        );
        Assert.Equal("visibility", Action.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public async Task The_query_carries_the_viewer_mail_visibility()
    {
        var mailbox = Guid.NewGuid();
        var restrictTo = Guid.NewGuid();
        var file = Guid.NewGuid();
        var (controller, bus) = Controller([mailbox], restrictTo);

        var result = await controller.Visibility(
            new FileVisibilityRequest([file], FileVisibilityPurposes.Download),
            CancellationToken.None
        );

        Assert.IsType<FileVisibilityResponse>(Assert.IsType<OkObjectResult>(result).Value);
        var query = Assert.IsType<GetVisibleMailFilesQuery>(bus.LastInvoked);
        Assert.Equal(Tenant, query.TenantId);
        Assert.Equal([file], query.FileIds);
        Assert.Equal([mailbox], query.VisibleAccountIds);
        Assert.Equal(restrictTo, query.RestrictToUserId);
        Assert.Equal(Viewer, query.ViewerUserId);
        Assert.True(query.CanDownloadIncoming);
        Assert.Equal(FileVisibilityPurposes.Download, query.Purpose);
    }

    [Fact]
    public async Task Without_attachment_download_the_query_says_so()
    {
        var (controller, bus) = Controller(canDownloadAttachments: false);

        await controller.Visibility(new FileVisibilityRequest([Guid.NewGuid()]), CancellationToken.None);

        Assert.False(Assert.IsType<GetVisibleMailFilesQuery>(bus.LastInvoked).CanDownloadIncoming);
    }

    private sealed class GrantedPermissions(string[] granted) : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(granted.Contains(permission));
    }

    public static TheoryData<FileVisibilityRequest?> InvalidRequests =>
        new()
        {
            null,
            new FileVisibilityRequest([]),
            new FileVisibilityRequest(
                Enumerable.Range(0, FileVisibilityLimits.Batch + 1).Select(_ => Guid.NewGuid()).ToList()
            ),
            new FileVisibilityRequest([Guid.NewGuid()], "export"),
        };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_requests_are_rejected(FileVisibilityRequest? request)
    {
        var (controller, bus) = Controller();

        var result = await controller.Visibility(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Correspondence.Files.InvalidVisibilityRequest", Assert.IsType<Error>(bad.Value).Code);
        Assert.Null(bus.LastInvoked);
    }
}
