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
using TaxVision.Notes.Api.Controllers;
using TaxVision.Notes.Application.Notes.Queries;
using Wolverine;

namespace TaxVision.Notes.Tests.Api;

/// <summary>
/// Visibilidad por archivo para la biblioteca: solo staff de la oficina con <c>notes.read</c>, cuota propia, identidad
/// del token, sin repetidos y tope de 100.
/// </summary>
public sealed class NoteFilesControllerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void Only_office_staff_with_notes_read_and_its_own_rate_limit()
    {
        var type = typeof(NoteFilesController);
        Assert.Equal("notes/files", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(
            [ActorType.TenantEmployee, ActorType.TenantAdmin],
            type.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );

        var action = type.GetMethod(nameof(NoteFilesController.Visibility))!;
        Assert.Equal("visibility", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(
            [NotesPermissions.Read],
            action
                .GetCustomAttributes<HasPermissionAttribute>()
                .Select(attribute => attribute.Policy![HasPermissionAttribute.PolicyPrefix.Length..])
        );
        Assert.Equal("notes.f.file_visibility", action.GetCustomAttribute<RateLimitAttribute>()!.PolicyName);
    }

    [Fact]
    public async Task The_query_carries_the_token_identity_and_the_ids_without_repeats()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var (controller, bus) = Build(NotesPermissions.ViewAll, CustomersPermissions.ViewAll);

        var result = await controller.Visibility(
            new FileVisibilityRequest([a, b, a], "download"),
            CancellationToken.None
        );

        Assert.IsType<FileVisibilityResponse>(Assert.IsType<OkObjectResult>(result).Value);
        var query = Assert.IsType<GetVisibleNoteFilesQuery>(bus.LastMessage);
        Assert.Equal(
            (Tenant, Actor, true, true),
            (query.TenantId, query.ActorUserId, query.ActorHasViewAll, query.CanViewAllCustomers)
        );
        Assert.Equal([a, b], query.FileIds);
    }

    [Fact]
    public async Task Without_the_view_all_permissions_the_query_says_so()
    {
        var (controller, bus) = Build();

        await controller.Visibility(new FileVisibilityRequest([Guid.NewGuid()]), CancellationToken.None);

        var query = Assert.IsType<GetVisibleNoteFilesQuery>(bus.LastMessage);
        Assert.False(query.ActorHasViewAll);
        Assert.False(query.CanViewAllCustomers);
    }

    public static TheoryData<FileVisibilityRequest?> InvalidRequests =>
        [
            null,
            new FileVisibilityRequest(null!),
            new FileVisibilityRequest([]),
            new FileVisibilityRequest(
                Enumerable.Range(0, FileVisibilityLimits.Batch + 1).Select(_ => Guid.NewGuid()).ToList()
            ),
            new FileVisibilityRequest([Guid.NewGuid()], "preview"),
        ];

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task An_invalid_request_is_a_bad_request(FileVisibilityRequest? request)
    {
        var (controller, bus) = Build();

        var result = await controller.Visibility(request, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("Notes.Files.InvalidVisibilityRequest", Assert.IsType<Error>(problem.Value).Code);
        Assert.Null(bus.LastMessage);
    }

    // ----- helpers -----

    private static (NoteFilesController Controller, AnsweringBus Bus) Build(params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimNames.TenantId, Tenant.ToString()),
            new("sub", Actor.ToString()),
            new(ClaimNames.ActorType, nameof(ActorType.TenantEmployee)),
        };
        claims.AddRange(permissions.Select(permission => new Claim(ClaimNames.Permission, permission)));
        var bus = AnsweringBus.Create();
        var controller = new NoteFilesController(bus, new JwtEmbeddedPermissionsSource())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
        return (controller, (AnsweringBus)(object)bus);
    }

    /// <summary>Bus que registra el último InvokeAsync y responde una lista vacía.</summary>
    public class AnsweringBus : DispatchProxy
    {
        public object? LastMessage { get; private set; }

        public static IMessageBus Create() => DispatchProxy.Create<IMessageBus, AnsweringBus>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is not { Name: nameof(IMessageBus.InvokeAsync), IsGenericMethod: true })
                throw new NotSupportedException(targetMethod?.Name);

            LastMessage = args![0];
            return Task.FromResult<IReadOnlyList<VisibleFile>>([]);
        }
    }
}
