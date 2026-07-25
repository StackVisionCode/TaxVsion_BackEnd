using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.ResourceAuthorization;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Api.Controllers;
using TaxVision.CloudStorage.Domain.Sharing;
using TaxVision.CloudStorage.Tests.Application;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;
using Xunit;

namespace TaxVision.CloudStorage.Tests.Api;

/// <summary>
/// RBAC Fase 4 (RBAC_Hardening_Plan.md) — prueba end-to-end (sin TestServer, sin precedente de
/// WebApplicationFactory en el repo) el chequeo de ownership de <see cref="ShareLinksController.Revoke"/>
/// bajo el feature flag <c>Authorization:ResourceOwnership:Enabled</c>. Cubre las 3 ramas: flag
/// apagado (comportamiento actual, sin cambios), dueño autorizado, no-dueño rechazado con 403.
/// </summary>
public sealed class ShareLinksControllerOwnershipTests
{
    private sealed class ControllableMessageBus : IMessageBus
    {
        public bool WasInvoked { get; private set; }
        public object? NextResult { get; set; }

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            WasInvoked = true;
            return Task.FromResult((T)NextResult!);
        }

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

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose() { }
        }
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

    private static ShareLinksController BuildController(
        ShareLink link,
        Guid actingUserId,
        bool flagEnabled,
        bool asPlatformAdmin = false
    )
    {
        var repo = new FakeShareLinkRepository();
        repo.Seed(link);
        var bus = new ControllableMessageBus { NextResult = Result.Success() };
        var authorizationService = new RealAuthorizationService(
            new IsOwnerOrHasManageHandler<ShareLink>(
                CloudStoragePermissions.ShareManage,
                new JwtEmbeddedPermissionsSource(),
                new AuthorizationMetrics()
            )
        );

        var claims = new List<Claim>
        {
            new("tenant_id", link.TenantId.ToString()),
            new("sub", actingUserId.ToString()),
        };
        if (asPlatformAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "PlatformAdmin"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var controller = new ShareLinksController(
            bus,
            new FakeCorrelationContext(),
            repo,
            authorizationService,
            new FakeOwnershipOptionsMonitor(flagEnabled),
            new JwtEmbeddedPermissionsSource()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
        return controller;
    }

    private static ShareLink NewShareLink(Guid tenantId, Guid createdByUserId)
    {
        var now = DateTime.UtcNow;
        var result = ShareLink.Create(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            ShareResourceType.File,
            ShareVisibility.TenantOnly,
            SharePermission.View,
            passwordHash: null,
            expiresAtUtc: now.AddDays(1),
            maxAccessCount: null,
            createdByUserId,
            now
        );
        return result.Value.Link;
    }

    [Fact]
    public async Task Revoke_bypasses_ownership_check_when_flag_is_disabled()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var link = NewShareLink(tenantId, ownerId);
        var controller = BuildController(link, actingUserId: Guid.NewGuid(), flagEnabled: false);

        var result = await controller.Revoke(link.Id, CancellationToken.None);

        Assert.IsNotType<ObjectResult>(result); // no 403 Error object — falls through to bus.InvokeAsync's NoContent()
    }

    [Fact]
    public async Task Revoke_allows_the_links_creator()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var link = NewShareLink(tenantId, ownerId);
        var controller = BuildController(link, actingUserId: ownerId, flagEnabled: true);

        var result = await controller.Revoke(link.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Revoke_rejects_a_non_owner_without_the_manage_permission()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var link = NewShareLink(tenantId, ownerId);
        var controller = BuildController(link, actingUserId: Guid.NewGuid(), flagEnabled: true);

        var result = await controller.Revoke(link.Id, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("ShareLink.NotOwner", error.Code);
    }

    [Fact]
    public async Task Revoke_always_allows_PlatformAdmin_regardless_of_ownership()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var link = NewShareLink(tenantId, ownerId);
        var controller = BuildController(link, actingUserId: Guid.NewGuid(), flagEnabled: true, asPlatformAdmin: true);

        var result = await controller.Revoke(link.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }
}
