using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notes.Api.Controllers;
using TaxVision.Notes.Application.Notes.Queries;
using Wolverine;

namespace TaxVision.Notes.Tests.Api;

/// <summary>Títulos en vivo del historial: solo notas, sin repetidos, tope del contrato y visibilidad del JWT.</summary>
public sealed class NotesWorkControllerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task Titles_keep_only_notes_without_repeats_with_the_viewer_visibility()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var (controller, bus) = Build(NotesPermissions.ViewAll);
        var request = new WorkTitlesRequest([
            new WorkTitleRef(WorkResourceTypes.Note, a),
            new WorkTitleRef(WorkResourceTypes.Invoice, Guid.NewGuid()),
            new WorkTitleRef(WorkResourceTypes.Note, b),
            new WorkTitleRef(WorkResourceTypes.Note, a),
        ]);

        var result = await controller.Titles(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<GetNoteWorkTitlesQuery>(bus.LastMessage);
        Assert.Equal((Tenant, Actor), (query.TenantId, query.ActorUserId));
        Assert.True(query.ActorHasViewAll);
        Assert.False(query.CanViewAllCustomers);
        Assert.Equal([a, b], query.Ids);
    }

    [Fact]
    public async Task Titles_with_only_foreign_types_answer_empty_without_reading()
    {
        var (controller, bus) = Build();
        var request = new WorkTitlesRequest([new WorkTitleRef(WorkResourceTypes.Task, Guid.NewGuid())]);

        var result = await controller.Titles(request, CancellationToken.None);

        var body = Assert.IsType<WorkTitlesResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(body.Items);
        Assert.Null(bus.LastMessage);
    }

    [Fact]
    public async Task Titles_reject_more_than_the_contract_limit_or_a_missing_body()
    {
        var (controller, bus) = Build();
        var tooMany = new WorkTitlesRequest(
            Enumerable
                .Range(0, WorkLimits.TitleBatch + 1)
                .Select(_ => new WorkTitleRef(WorkResourceTypes.Note, Guid.NewGuid()))
                .ToList()
        );

        var overLimit = await controller.Titles(tooMany, CancellationToken.None);
        var missing = await controller.Titles(null, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(overLimit).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(missing).StatusCode);
        Assert.Null(bus.LastMessage);
    }

    [Fact]
    public void Titles_require_staff_the_notes_read_permission_and_the_list_rate_limit()
    {
        var type = typeof(NotesWorkController);
        Assert.Equal("notes/work", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(
            [ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin],
            type.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );

        var action = type.GetMethod(nameof(NotesWorkController.Titles))!;
        Assert.Equal("titles", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(
            [NotesPermissions.Read],
            action
                .GetCustomAttributes<HasPermissionAttribute>()
                .Select(attribute => attribute.Policy![HasPermissionAttribute.PolicyPrefix.Length..])
        );
        Assert.Equal("notes.f.list", action.GetCustomAttribute<RateLimitAttribute>()!.PolicyName);
    }

    // ----- helpers -----

    private static (NotesWorkController Controller, AnsweringBus Bus) Build(params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimNames.TenantId, Tenant.ToString()),
            new("sub", Actor.ToString()),
            new(ClaimNames.ActorType, nameof(ActorType.TenantEmployee)),
        };
        claims.AddRange(permissions.Select(permission => new Claim(ClaimNames.Permission, permission)));
        var bus = AnsweringBus.Create();
        var controller = new NotesWorkController(bus, new JwtEmbeddedPermissionsSource())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
        return (controller, (AnsweringBus)(object)bus);
    }

    /// <summary>Bus que registra el último InvokeAsync y responde una lista de títulos vacía.</summary>
    public class AnsweringBus : DispatchProxy
    {
        public object? LastMessage { get; private set; }

        public static IMessageBus Create() => DispatchProxy.Create<IMessageBus, AnsweringBus>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is not { Name: nameof(IMessageBus.InvokeAsync), IsGenericMethod: true })
                throw new NotSupportedException(targetMethod?.Name);

            LastMessage = args![0];
            return Task.FromResult(new WorkTitlesResponse([]));
        }
    }
}
