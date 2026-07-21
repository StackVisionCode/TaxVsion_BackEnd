using System.Security.Claims;
using BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Http;
using TaxVision.Growth.Api.Common;
using Wolverine;

namespace TaxVision.Growth.Tests.Security;

public sealed class JwtTenantContextMiddlewareTests
{
    [Fact]
    public async Task Validated_tenant_claim_establishes_the_request_tenant()
    {
        var expectedTenant = Guid.NewGuid();
        var httpContext = AuthenticatedContext(new Claim("tenant_id", expectedTenant.ToString()));
        var tenantContext = new TenantContext();
        var bus = new FakeMessageBus();
        var nextCalled = false;
        var middleware = new JwtTenantContextMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext, tenantContext, bus);

        Assert.True(nextCalled);
        Assert.True(tenantContext.HasTenant);
        Assert.Equal(expectedTenant, tenantContext.TenantId);
        Assert.Equal(expectedTenant.ToString(), bus.TenantId);
    }

    [Fact]
    public async Task Malformed_tenant_claim_fails_closed_before_the_pipeline()
    {
        var httpContext = AuthenticatedContext(new Claim("tenant_id", "not-a-guid"));
        var tenantContext = new TenantContext();
        var bus = new FakeMessageBus();
        var nextCalled = false;
        var middleware = new JwtTenantContextMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext, tenantContext, bus);

        Assert.False(nextCalled);
        Assert.False(tenantContext.HasTenant);
        Assert.Null(bus.TenantId);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
    }

    [Fact]
    public void Local_command_envelope_restores_tenant_in_the_handler_scope()
    {
        var expectedTenant = Guid.NewGuid();
        var envelope = new Envelope { TenantId = expectedTenant.ToString() };
        var tenantContext = new TenantContext();

        GrowthLocalCommandTenantMiddleware.Before(envelope, tenantContext);

        Assert.True(tenantContext.HasTenant);
        Assert.Equal(expectedTenant, tenantContext.TenantId);
    }

    [Fact]
    public void Envelope_without_a_tenant_id_leaves_tenant_context_empty()
    {
        var envelope = new Envelope();
        var tenantContext = new TenantContext();

        GrowthLocalCommandTenantMiddleware.Before(envelope, tenantContext);

        Assert.False(tenantContext.HasTenant);
    }

    [Fact]
    public void Integration_message_without_tenant_is_rejected()
    {
        var message = new TenantlessIntegrationEvent();
        var tenantContext = new TenantContext();

        Assert.Throws<InvalidOperationException>(() => GrowthTenantMessageMiddleware.Before(message, tenantContext));
        Assert.False(tenantContext.HasTenant);
    }

    private static DefaultHttpContext AuthenticatedContext(params Claim[] claims)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return context;
    }

    private sealed record TenantlessIntegrationEvent : BuildingBlocks.Messaging.IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
        public Guid TenantId { get; init; } = Guid.Empty;
        public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    }

    /// <summary>Minimal IMessageBus test double — only TenantId is exercised by these tests.</summary>
    private sealed class FakeMessageBus : IMessageBus
    {
        public string? TenantId { get; set; }

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

        public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotImplementedException();

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();
    }
}
