using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Controllers;
using TaxVision.CloudStorage.Domain.Files;
using Wolverine;

namespace TaxVision.CloudStorage.Tests.Api;

/// <summary>CST-14: un archivo en la papelera no se mueve y el CRM recibe un conflicto de estado.</summary>
public sealed class FilesControllerMoveToFolderTests
{
    [Fact]
    public async Task MoveToFolder_SoftDeletedFile_Returns409()
    {
        var result = await MoveAsync(Result.Failure(FileErrors.InvalidTransition));

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task MoveToFolder_MissingFile_KeepsItsStatus()
    {
        var result = await MoveAsync(Result.Failure(FileErrors.NotFound));

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    private static Task<IActionResult> MoveAsync(Result response)
    {
        var bus = DispatchProxy.Create<IMessageBus, FixedResultBus>();
        ((FixedResultBus)(object)bus).Response = response;
        var correlation = DispatchProxy.Create<ICorrelationContext, EmptyProxy>();
        var controller = new FilesController(bus, correlation, null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [
                                new Claim("tenant_id", Guid.NewGuid().ToString()),
                                new Claim("sub", Guid.NewGuid().ToString()),
                            ],
                            "test"
                        )
                    ),
                },
            },
        };
        return controller.MoveToFolder(Guid.NewGuid(), new FilesController.MoveToFolderRequest(null), default);
    }

    public class FixedResultBus : DispatchProxy
    {
        public Result Response { get; set; } = Result.Success();

        protected override object Invoke(MethodInfo? targetMethod, object?[]? args) => Task.FromResult(Response);
    }

    public class EmptyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
