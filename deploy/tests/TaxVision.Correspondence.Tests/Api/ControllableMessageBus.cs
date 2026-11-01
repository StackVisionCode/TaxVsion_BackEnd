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

/// <summary>Bus de prueba: registra el último mensaje y devuelve el resultado preparado.</summary>
internal sealed class ControllableMessageBus : IMessageBus
{
    public object? NextResult { get; set; }

    public object? LastInvoked { get; private set; }

    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
    {
        LastInvoked = message;
        return Task.FromResult((T)NextResult!);
    }

    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) => ValueTask.CompletedTask;

    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw new NotImplementedException();

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
