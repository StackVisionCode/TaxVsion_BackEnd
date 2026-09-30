using System.Text;
using System.Text.Json;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Api.Controllers;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.PaymentApp.Tests.Api;

/// <summary>
/// Un webhook throttleado por tenant debe responder 429 (Stripe/PayPal lo reintentan). Con 200 el
/// provider daba el evento por entregado y un pago confirmado se perdía.
/// </summary>
public sealed class ProviderWebhookControllersTests
{
    private static readonly Result Throttled = Result.Failure(
        new Error(ProcessProviderWebhookHandler.WebhookThrottledCode, "Too many webhook events for this tenant.")
    );

    [Fact]
    public async Task Stripe_throttled_webhook_answers_429_with_retry_after()
    {
        var context = NewContext();
        var controller = new StripeWebhookController(new ResultBus(Throttled))
        {
            ControllerContext = new() { HttpContext = context },
        };

        var action = await controller.Receive(CancellationToken.None);

        Assert.IsType<EmptyResult>(action);
        AssertThrottledResponse(context);
    }

    [Fact]
    public async Task PayPal_throttled_webhook_answers_429_with_retry_after()
    {
        var context = NewContext();
        var controller = new PayPalWebhookController(new ResultBus(Throttled))
        {
            ControllerContext = new() { HttpContext = context },
        };

        var action = await controller.Receive(CancellationToken.None);

        Assert.IsType<EmptyResult>(action);
        AssertThrottledResponse(context);
    }

    [Fact]
    public async Task Stripe_invalid_signature_still_answers_400()
    {
        var context = NewContext();
        var invalid = Result.Failure(new Error("Stripe.Webhook.InvalidSignature", "Invalid signature."));
        var controller = new StripeWebhookController(new ResultBus(invalid))
        {
            ControllerContext = new() { HttpContext = context },
        };

        var action = await controller.Receive(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action);
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void AssertThrottledResponse(HttpContext context)
    {
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal(
            ProcessProviderWebhookHandler.WebhookThrottleRetryAfterSeconds.ToString(),
            context.Response.Headers.RetryAfter.ToString()
        );
        context.Response.Body.Position = 0;
        var body = JsonDocument.Parse(context.Response.Body).RootElement;
        Assert.Equal("RateLimit.Exceeded", body.GetProperty("code").GetString());
        Assert.Equal("paymentapp.webhook_tenant", body.GetProperty("policy").GetString());
    }

    /// <summary>Bus que responde a <c>InvokeAsync&lt;Result&gt;</c> con un resultado fijo.</summary>
    private sealed class ResultBus(Result result) : IMessageBus
    {
        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => Task.FromResult((T)(object)result);

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => Task.FromResult((T)(object)result);

        public string? TenantId { get; set; }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotSupportedException();

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw new NotSupportedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotSupportedException();

        public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotSupportedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotSupportedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotSupportedException();
    }
}
