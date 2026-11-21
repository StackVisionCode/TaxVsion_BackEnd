using System.Reflection;
using BuildingBlocks.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Controllers;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;
using static TaxVision.CloudStorage.Tests.Api.FilesControllerScopeTests;

namespace TaxVision.CloudStorage.Tests.Api;

/// <summary>Compartir un archivo por id usa el alcance resuelto: sin permiso del origen no hay enlace.</summary>
public sealed class ShareLinksControllerScopeTests
{
    [Fact]
    public async Task Creating_a_file_share_sends_the_resolved_scope()
    {
        var (controller, bus) = NewController();
        var request = new ShareLinksController.CreateShareLinkRequest(
            ShareVisibility.TenantOnly,
            SharePermission.View,
            null,
            null,
            null,
            null,
            null,
            null
        );

        await Assert.ThrowsAsync<StopException>(() => controller.Create(Guid.NewGuid(), request, default));

        AssertResolved(bus);
    }

    [Fact]
    public async Task Listing_the_shares_of_a_file_sends_the_resolved_scope()
    {
        var (controller, bus) = NewController();

        await Assert.ThrowsAsync<StopException>(() => controller.ListForFile(Guid.NewGuid(), default));

        AssertResolved(bus);
    }

    [Fact]
    public async Task Creating_a_folder_share_sends_the_resolved_scope()
    {
        var (controller, bus) = NewController();
        var request = new ShareLinksController.CreateFolderShareLinkRequest(
            ShareVisibility.TenantOnly,
            SharePermission.View,
            null,
            null,
            null,
            false,
            false,
            null,
            null,
            null
        );

        await Assert.ThrowsAsync<StopException>(() => controller.CreateForFolder(Guid.NewGuid(), request, default));

        AssertResolved(bus);
    }

    [Fact]
    public async Task Listing_the_shares_of_a_folder_sends_the_resolved_scope()
    {
        var (controller, bus) = NewController();

        await Assert.ThrowsAsync<StopException>(() => controller.ListForFolder(Guid.NewGuid(), default));

        AssertResolved(bus);
    }

    private static void AssertResolved(CapturingBus bus)
    {
        var scope = Assert.IsType<StorageActorScope>(
            bus.Message!.GetType().GetProperty("Scope")!.GetValue(bus.Message)
        );
        Assert.False(scope.BypassAssignmentFilter);
    }

    private static (ShareLinksController Controller, CapturingBus Bus) NewController()
    {
        var bus = DispatchProxy.Create<IMessageBus, CapturingBus>();
        var correlation = DispatchProxy.Create<ICorrelationContext, EmptyProxy>();
        var controller = new ShareLinksController(
            bus,
            correlation,
            null!,
            null!,
            null!,
            new StorageActorScopeResolverTests.RecordingPermissions(),
            new MarkingResolver()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = StaffUser() } },
        };
        return (controller, (CapturingBus)(object)bus);
    }
}
