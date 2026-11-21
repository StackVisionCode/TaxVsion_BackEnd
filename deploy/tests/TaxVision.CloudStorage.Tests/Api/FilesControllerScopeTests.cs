using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Api.Controllers;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Domain.Files;
using Wolverine;

namespace TaxVision.CloudStorage.Tests.Api;

/// <summary>Los endpoints por id mandan al handler el alcance resuelto (asignación y compuerta por origen), no el del JWT.</summary>
public sealed class FilesControllerScopeTests
{
    private static readonly Guid Marker = Guid.NewGuid();

    public static TheoryData<string> ByIdActions => ["GetById", "IssueDownloadUrl", "Delete", "MoveToFolder", "Copy"];

    [Theory]
    [MemberData(nameof(ByIdActions))]
    public async Task By_id_actions_send_the_resolved_scope(string action)
    {
        var (controller, bus) = NewController();

        await Assert.ThrowsAsync<StopException>(() => Invoke(controller, action));

        var scope = Assert.IsType<StorageActorScope>(
            bus.Message!.GetType().GetProperty("Scope")!.GetValue(bus.Message)
        );
        Assert.Contains(Marker, scope.AssignedCustomerIds);
    }

    private static Task<IActionResult> Invoke(FilesController controller, string action) =>
        action switch
        {
            "GetById" => controller.GetById(Guid.NewGuid(), default),
            "IssueDownloadUrl" => controller.IssueDownloadUrl(Guid.NewGuid(), null, default),
            "Delete" => controller.Delete(Guid.NewGuid(), default),
            "MoveToFolder" => controller.MoveToFolder(
                Guid.NewGuid(),
                new FilesController.MoveToFolderRequest(null),
                default
            ),
            "Copy" => controller.Copy(
                Guid.NewGuid(),
                new CopyFileRequest(OwnerType.Customer, Guid.NewGuid(), FolderType.Documents, null),
                default
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    private static (FilesController Controller, CapturingBus Bus) NewController()
    {
        var bus = DispatchProxy.Create<IMessageBus, CapturingBus>();
        var correlation = DispatchProxy.Create<ICorrelationContext, EmptyProxy>();
        var controller = new FilesController(bus, correlation, null!, null!, new MarkingResolver())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = StaffUser() } },
        };
        return (controller, (CapturingBus)(object)bus);
    }

    internal static ClaimsPrincipal StaffUser() =>
        new(
            new ClaimsIdentity(
                [
                    new Claim("tenant_id", Guid.NewGuid().ToString()),
                    new Claim("sub", Guid.NewGuid().ToString()),
                    new Claim("actor_type", "TenantEmployee"),
                ],
                "test"
            )
        );

    /// <summary>Marca el alcance para poder distinguirlo del que sale del JWT.</summary>
    internal sealed class MarkingResolver : IStorageActorScopeResolver
    {
        public Task<StorageActorScope> ResolveAsync(
            ClaimsPrincipal user,
            Guid tenantId,
            Guid actorId,
            StorageActorScope baseScope,
            CancellationToken ct
        ) => Task.FromResult(baseScope with { BypassAssignmentFilter = false, AssignedCustomerIds = [Marker] });
    }

    /// <summary>Guarda el mensaje enviado y corta la acción con una excepción propia.</summary>
    public class CapturingBus : DispatchProxy
    {
        public object? Message { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Message = args?[0];
            var resultType = targetMethod!.ReturnType.GetGenericArguments()[0];
            return typeof(Task)
                .GetMethods()
                .Single(m => m.Name == nameof(Task.FromException) && m.IsGenericMethod)
                .MakeGenericMethod(resultType)
                .Invoke(null, [new StopException()]);
        }
    }

    public class EmptyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }

    public sealed class StopException : Exception;
}
