using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Sms.Api.Controllers;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Tests.Fakes;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;
using static TaxVision.Sms.Api.Controllers.MessagesController;

namespace TaxVision.Sms.Tests.Api;

/// <summary>POST /sms/messages: cada petición lleva su propia clave; un reenvío legítimo no se confunde con un reintento.</summary>
public sealed class MessagesControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static SendMessagesRequest SameText() =>
        new([new MessageItemRequest(CustomerId, "+18095551234", "Your return is ready", null, null, null, "Ana")]);

    [Fact]
    public async Task Same_text_sent_in_two_requests_gets_two_keys()
    {
        var bus = new CapturingBus();

        await BuildController(bus).Send(SameText(), CancellationToken.None);
        await BuildController(bus).Send(SameText(), CancellationToken.None);

        Assert.Equal(2, bus.Commands.Count);
        var first = Assert.Single(bus.Commands[0].Items).IdempotencyKey;
        var second = Assert.Single(bus.Commands[1].Items).IdempotencyKey;
        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.False(string.IsNullOrWhiteSpace(second));
        Assert.NotEqual(first, second);
        Assert.StartsWith("crm:", first);
    }

    private static MessagesController BuildController(CapturingBus bus, string? idempotencyKeyHeader = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimNames.TenantId, TenantId.ToString()),
            new("sub", Guid.NewGuid().ToString()),
        };
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
        if (idempotencyKeyHeader is not null)
            http.Request.Headers["Idempotency-Key"] = idempotencyKeyHeader;

        return new MessagesController(bus, new FixedTenant(), new FakeCorrelationContext(), new AllowAll())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private sealed class FixedTenant : ITenantContext
    {
        public Guid TenantId => MessagesControllerTests.TenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private sealed class AllowAll : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    /// <summary>Captura los SendSmsBatchCommand y responde un lote vacío.</summary>
    private sealed class CapturingBus : IMessageBus
    {
        public List<SendSmsBatchCommand> Commands { get; } = [];

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            var command = (SendSmsBatchCommand)message;
            Commands.Add(command);
            object result = Result.Success(new SendSmsBatchResponse(Guid.NewGuid(), command.CorrelationId, []));
            return Task.FromResult((T)result);
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
}
