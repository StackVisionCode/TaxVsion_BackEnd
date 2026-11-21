using System.Reflection;
using BuildingBlocks.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Controllers;
using TaxVision.CloudStorage.Application.Abstractions;
using Wolverine;
using static TaxVision.CloudStorage.Tests.Api.FilesControllerScopeTests;

namespace TaxVision.CloudStorage.Tests.Api;

/// <summary>La papelera lista y restaura con el alcance resuelto de la persona.</summary>
public sealed class RecycleBinControllerScopeTests
{
    [Fact]
    public async Task Listing_the_bin_sends_the_resolved_scope()
    {
        var (controller, bus) = NewController();

        await Assert.ThrowsAsync<StopException>(() => controller.List(0, 50, default));

        AssertResolved(bus);
    }

    [Fact]
    public async Task Restoring_a_file_sends_the_resolved_scope()
    {
        var (controller, bus) = NewController();

        await Assert.ThrowsAsync<StopException>(() => controller.Restore(Guid.NewGuid(), default));

        AssertResolved(bus);
    }

    private static void AssertResolved(CapturingBus bus)
    {
        var scope = Assert.IsType<StorageActorScope>(
            bus.Message!.GetType().GetProperty("Scope")!.GetValue(bus.Message)
        );
        Assert.False(scope.BypassAssignmentFilter);
    }

    private static (RecycleBinController Controller, CapturingBus Bus) NewController()
    {
        var bus = DispatchProxy.Create<IMessageBus, CapturingBus>();
        var correlation = DispatchProxy.Create<ICorrelationContext, EmptyProxy>();
        var controller = new RecycleBinController(bus, correlation, new MarkingResolver())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = StaffUser() } },
        };
        return (controller, (CapturingBus)(object)bus);
    }
}
