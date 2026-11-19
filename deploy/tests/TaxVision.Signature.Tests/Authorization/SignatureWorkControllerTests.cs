using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Api.Controllers;
using TaxVision.Signature.Application.Requests.Queries.Work;
using Wolverine;

namespace TaxVision.Signature.Tests.Authorization;

/// <summary>
/// Work Profile: el propio trabajo con el permiso de lectura del módulo; el de otra persona y el lote exigen además
/// <c>users.work.view</c>. Tenant y visibilidad siempre del JWT.
/// </summary>
public sealed class SignatureWorkControllerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task Own_work_needs_only_the_module_read_permission()
    {
        var (controller, bus) = Build();

        var result = await controller.Get(Actor, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<GetSignatureWorkQuery>(bus.LastMessage);
        Assert.Equal(new GetSignatureWorkQuery(Tenant, Actor, Actor, CanViewAll: false), query);
    }

    [Fact]
    public async Task Another_member_without_users_work_view_is_forbidden()
    {
        var (controller, bus) = Build();

        var result = await controller.Get(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Null(bus.LastMessage);
    }

    [Fact]
    public async Task Another_member_with_users_work_view_is_allowed_with_the_viewer_visibility()
    {
        var member = Guid.NewGuid();
        var (controller, bus) = Build(UserManagementPermissions.UsersWorkView, CustomersPermissions.ViewAll);

        var result = await controller.Get(member, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new GetSignatureWorkQuery(Tenant, member, Actor, CanViewAll: true), bus.LastMessage);
    }

    [Fact]
    public async Task Batch_parses_the_ids_with_the_tenant_of_the_token()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var (controller, bus) = Build(UserManagementPermissions.UsersWorkView);

        var result = await controller.Summary($"{a},{b},{a}", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<GetSignatureWorkBatchQuery>(bus.LastMessage);
        Assert.Equal(Tenant, query.TenantId);
        Assert.Equal([a, b], query.UserIds);
        Assert.Equal(Actor, query.ActorUserId);
        Assert.False(query.CanViewAll);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task Batch_rejects_invalid_ids(string userIds)
    {
        var (controller, bus) = Build(UserManagementPermissions.UsersWorkView);

        var result = await controller.Summary(userIds, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Null(bus.LastMessage);
    }

    [Fact]
    public async Task Batch_rejects_more_than_the_contract_limit()
    {
        var (controller, bus) = Build(UserManagementPermissions.UsersWorkView);
        var ids = string.Join(',', Enumerable.Range(0, WorkLimits.BatchUsers + 1).Select(_ => Guid.NewGuid()));

        var result = await controller.Summary(ids, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Null(bus.LastMessage);
    }

    [Fact]
    public void Endpoints_declare_actor_types_module_read_and_users_work_view_on_the_batch()
    {
        var type = typeof(SignatureWorkController);
        var actors = type.GetCustomAttribute<AllowActorTypesAttribute>()!;
        Assert.Equal([ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin], actors.ActorTypes);

        Assert.Equal([SignaturePermissions.RequestRead], Permissions(nameof(SignatureWorkController.Get)));
        Assert.Equal(
            [SignaturePermissions.RequestRead, UserManagementPermissions.UsersWorkView],
            Permissions(nameof(SignatureWorkController.Summary))
        );
    }

    // ----- helpers -----

    private static string[] Permissions(string action) =>
        typeof(SignatureWorkController)
            .GetMethod(action)!
            .GetCustomAttributes<HasPermissionAttribute>()
            .Select(attribute => attribute.Policy![HasPermissionAttribute.PolicyPrefix.Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static (SignatureWorkController Controller, AnsweringBus Bus) Build(params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimNames.TenantId, Tenant.ToString()),
            new("sub", Actor.ToString()),
            new(ClaimNames.ActorType, nameof(ActorType.TenantEmployee)),
        };
        claims.AddRange(permissions.Select(permission => new Claim(ClaimNames.Permission, permission)));
        var bus = AnsweringBus.Create();
        var controller = new SignatureWorkController(bus, new JwtEmbeddedPermissionsSource())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
        return (controller, (AnsweringBus)(object)bus);
    }

    /// <summary>Bus que registra el último InvokeAsync y responde un resumen o un lote vacío.</summary>
    public class AnsweringBus : DispatchProxy
    {
        public object? LastMessage { get; private set; }

        public static IMessageBus Create() => DispatchProxy.Create<IMessageBus, AnsweringBus>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is not { Name: nameof(IMessageBus.InvokeAsync), IsGenericMethod: true })
                throw new NotSupportedException(targetMethod?.Name);

            LastMessage = args![0];
            var responseType = targetMethod.GetGenericArguments()[0];
            object response =
                responseType == typeof(WorkBatchResponse)
                    ? new WorkBatchResponse([])
                    : new WorkSummaryResponse("signature", "signatures", [], []);
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(responseType)
                .Invoke(null, [response]);
        }
    }
}
