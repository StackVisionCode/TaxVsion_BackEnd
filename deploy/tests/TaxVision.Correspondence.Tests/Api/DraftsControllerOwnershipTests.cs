using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.ResourceAuthorization;
using BuildingBlocks.Results;
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
    private sealed class ControllableMessageBus : IMessageBus
    {
        public object? NextResult { get; set; }

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => Task.FromResult((T)NextResult!);

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) => ValueTask.CompletedTask;

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public string? TenantId
        {
            get => null;
            set { }
        }

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();
    }

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
            claims.Add(new Claim(ClaimTypes.Role, "PlatformAdmin"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var controller = new DraftsController(
            bus,
            repo,
            authorizationService,
            new FakeOwnershipOptionsMonitor(flagEnabled)
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
}
