using System.Security.Claims;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.ResourceAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Api.Controllers;
using TaxVision.Correspondence.Api.Requests;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Tests.Compose;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;
using Xunit;

namespace TaxVision.Correspondence.Tests.Api;

/// <summary>
/// RBAC Fase 4 (RBAC_Hardening_Plan.md) — prueba end-to-end (sin TestServer) el chequeo de
/// ownership de <see cref="DraftsController.AutoSave"/> bajo el feature flag
/// <c>Authorization:ResourceOwnership:Enabled</c>. Draft no tiene permiso "manage" de override
/// (a diferencia de ShareLink/SignatureRequest, ver decisión documentada en Program.cs) — solo el
/// creador o PlatformAdmin pasan.
/// </summary>
public sealed class DraftsControllerOwnershipTests
{
    /// <summary>Wrapper real y liviano sobre <see cref="IsOwnerOrHasManageHandler{TResource}"/> — sin DI, sin ServiceProvider.</summary>
    private sealed class RealAuthorizationService(IAuthorizationHandler handler) : IAuthorizationService
    {
        public async Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements
        )
        {
            var context = new AuthorizationHandlerContext(requirements, user, resource);
            await handler.HandleAsync(context);
            return context.HasSucceeded ? AuthorizationResult.Success() : AuthorizationResult.Failed();
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IAuthorizationRequirement requirement
        ) => AuthorizeAsync(user, resource, [requirement]);

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            throw new NotImplementedException();
    }

    private sealed class FakeOwnershipOptionsMonitor(bool enabled) : IOptionsMonitor<ResourceOwnershipOptions>
    {
        public ResourceOwnershipOptions CurrentValue { get; } = new() { Enabled = enabled };

        public ResourceOwnershipOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<ResourceOwnershipOptions, string> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose() { }
        }
    }

    private static DraftsController BuildController(
        Draft draft,
        Guid actingUserId,
        bool flagEnabled,
        bool asPlatformAdmin = false
    )
    {
        var repo = new FakeDraftRepository();
        repo.AddAsync(draft, CancellationToken.None).GetAwaiter().GetResult();
        var bus = new ControllableMessageBus { NextResult = Result.Success() };
        var authorizationService = new RealAuthorizationService(
            new IsOwnerOrHasManageHandler<Draft>(null, new JwtEmbeddedPermissionsSource(), new AuthorizationMetrics())
        );

        var claims = new List<Claim>
        {
            new("tenant_id", draft.TenantId.ToString()),
            new("sub", actingUserId.ToString()),
        };
        if (asPlatformAdmin)
            claims.Add(new Claim("actor_type", "PlatformAdmin"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var controller = new DraftsController(
            bus,
            repo,
            authorizationService,
            new FakeOwnershipOptionsMonitor(flagEnabled),
            new Projections.FakeCorrelationContext(),
            new FakeMailboxVisibilityResolver(),
            new FakeCustomerAccessResolver()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
        return controller;
    }

    private static Draft NewDraft(Guid tenantId, Guid createdByUserId) =>
        Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), createdByUserId).Value;

    private static readonly AutoSaveDraftBody EmptyBody = new(null, null, null, null, null, null);

    [Fact]
    public async Task AutoSave_bypasses_ownership_check_when_flag_is_disabled()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var draft = NewDraft(tenantId, ownerId);
        var controller = BuildController(draft, actingUserId: Guid.NewGuid(), flagEnabled: false);

        var result = await controller.AutoSave(draft.Id, EmptyBody, CancellationToken.None);

        Assert.IsNotType<ObjectResult>(result);
    }

    [Fact]
    public async Task AutoSave_allows_the_drafts_creator()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var draft = NewDraft(tenantId, ownerId);
        var controller = BuildController(draft, actingUserId: ownerId, flagEnabled: true);

        var result = await controller.AutoSave(draft.Id, EmptyBody, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task AutoSave_rejects_a_non_owner()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var draft = NewDraft(tenantId, ownerId);
        var controller = BuildController(draft, actingUserId: Guid.NewGuid(), flagEnabled: true);

        var result = await controller.AutoSave(draft.Id, EmptyBody, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("Draft.NotOwner", error.Code);
    }

    [Fact]
    public async Task AutoSave_always_allows_PlatformAdmin_regardless_of_ownership()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var draft = NewDraft(tenantId, ownerId);
        var controller = BuildController(draft, actingUserId: Guid.NewGuid(), flagEnabled: true, asPlatformAdmin: true);

        var result = await controller.AutoSave(draft.Id, EmptyBody, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    // ---------- Listados: el alcance por cliente es explícito ----------

    [Theory]
    [InlineData(null, false)] // ninguno: un customerId olvidado no se lee como "sin cliente"
    [InlineData("8c1d3f7e-0000-4000-8000-000000000001", true)] // los dos a la vez
    [InlineData("00000000-0000-0000-0000-000000000000", false)] // id vacío
    public async Task Lists_reject_a_missing_or_ambiguous_customer_scope(string? customerId, bool unlinked)
    {
        var parsed = customerId is null ? (Guid?)null : Guid.Parse(customerId);
        var (controller, bus) = BuildListController();

        var results = new[]
        {
            await controller.List(parsed, unlinked, null, 1, 20, CancellationToken.None),
            await controller.ListSent(parsed, unlinked, null, 1, 20, CancellationToken.None),
            await controller.ListTrash(parsed, unlinked, null, 1, 20, CancellationToken.None),
        };

        Assert.All(
            results,
            result =>
            {
                var badRequest = Assert.IsType<BadRequestObjectResult>(result);
                Assert.Equal("Correspondence.CustomerScopeRequired", Assert.IsType<Error>(badRequest.Value).Code);
            }
        );
        Assert.Null(bus.LastInvoked);
    }

    [Fact]
    public async Task ListSent_with_unlinked_queries_mail_without_customer()
    {
        var (controller, bus) = BuildListController();
        bus.NextResult =
            new BuildingBlocks.Common.PagedResult<TaxVision.Correspondence.Application.Compose.SentMessageListItem>(
                [],
                1,
                20,
                0
            );

        var result = await controller.ListSent(null, true, null, 1, 20, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<TaxVision.Correspondence.Application.Compose.ListSentMessagesQuery>(bus.LastInvoked);
        Assert.True(query.Customer.IsUnlinked);
    }

    [Fact]
    public async Task ListSent_with_customerId_queries_that_customer()
    {
        var customerId = Guid.NewGuid();
        var (controller, bus) = BuildListController();
        bus.NextResult =
            new BuildingBlocks.Common.PagedResult<TaxVision.Correspondence.Application.Compose.SentMessageListItem>(
                [],
                1,
                20,
                0
            );

        await controller.ListSent(customerId, false, null, 1, 20, CancellationToken.None);

        var query = Assert.IsType<TaxVision.Correspondence.Application.Compose.ListSentMessagesQuery>(bus.LastInvoked);
        Assert.Equal(customerId, query.Customer.CustomerId);
    }

    [Fact]
    public async Task ListSent_with_scope_all_lists_every_recipient_and_keeps_the_customer_restriction()
    {
        var restrictedUser = Guid.NewGuid();
        var (controller, bus) = BuildListController(new FakeCustomerAccessResolver { RestrictTo = restrictedUser });
        bus.NextResult =
            new BuildingBlocks.Common.PagedResult<TaxVision.Correspondence.Application.Compose.SentMessageListItem>(
                [],
                1,
                20,
                0
            );

        var result = await controller.ListSent(null, false, "all", 1, 20, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<TaxVision.Correspondence.Application.Compose.ListSentMessagesQuery>(bus.LastInvoked);
        Assert.True(query.Customer.IsAll);
        Assert.False(query.Customer.IsUnlinked);
        Assert.Equal(restrictedUser, query.Customer.RestrictToUserId);
    }

    [Theory]
    [InlineData("all", "11111111-1111-1111-1111-111111111111", false)]
    [InlineData("all", null, true)]
    [InlineData("everything", null, false)]
    public async Task Scope_all_cannot_be_mixed_with_another_scope(string scopeName, string? customerId, bool unlinked)
    {
        var (controller, bus) = BuildListController();

        var result = await controller.ListTrash(
            customerId is null ? null : Guid.Parse(customerId),
            unlinked,
            scopeName,
            1,
            20,
            CancellationToken.None
        );

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(bus.LastInvoked);
    }

    private static (DraftsController Controller, ControllableMessageBus Bus) BuildListController(
        FakeCustomerAccessResolver? customerAccess = null
    )
    {
        var bus = new ControllableMessageBus();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim("sub", Guid.NewGuid().ToString())],
                "Test"
            )
        );
        var controller = new DraftsController(
            bus,
            new FakeDraftRepository(),
            new RealAuthorizationService(
                new IsOwnerOrHasManageHandler<Draft>(
                    null,
                    new JwtEmbeddedPermissionsSource(),
                    new AuthorizationMetrics()
                )
            ),
            new FakeOwnershipOptionsMonitor(false),
            new Projections.FakeCorrelationContext(),
            new FakeMailboxVisibilityResolver(),
            customerAccess ?? new FakeCustomerAccessResolver()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
        return (controller, bus);
    }
}
