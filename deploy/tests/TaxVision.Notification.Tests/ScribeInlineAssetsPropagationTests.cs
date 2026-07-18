using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Hardening Fase 9 — el tercer eslabón de la cadena: prueba que un consumer real
/// (<see cref="UserRegisteredConsumer"/>, representativo de los 11 call sites que renderizan vía
/// Scribe) efectivamente toma <c>ScribeRenderedEmail.InlineAssets</c> del resultado de
/// <see cref="IScribeRenderClient.RenderAsync"/> y lo reenvía en <c>EmailDispatchRequest.InlineAssets</c>
/// hacia el gateway — no solo que el tipo compile, sino que el dato realmente viaje. Complementa
/// <see cref="ScribeRenderClientTests"/> (deserialización HTTP) y
/// <c>EventBasedEmailDispatchGatewayTests.QueueEmailAsync_propagates_InlineAssets_from_request_to_published_event</c>
/// (gateway → evento) para cubrir toda la cadena Scribe → Notification → evento.
/// </summary>
public sealed class ScribeInlineAssetsPropagationTests
{
    [Fact]
    public async Task UserRegisteredConsumer_forwards_ScribeRenderedEmail_InlineAssets_to_EmailDispatchRequest()
    {
        var cloudStorageFileId = Guid.NewGuid();
        var scribeClient = new SucceedingScribeRenderClient(
            new ScribeRenderedEmail(
                "Welcome",
                "<p>Hi <img src=\"cid:logo-header\"/></p>",
                "Hi",
                [new EmailInlineAssetReference("logo-header", cloudStorageFileId, "image/png", 4096)]
            )
        );
        var gateway = new RecordingEmailDispatchGateway();
        var evt = new UserRegisteredIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Email = "new-user@test.com",
            ActorType = "User",
            Name = "Ana",
            LastName = "Perez",
        };

        await UserRegisteredConsumer.Handle(
            evt,
            gateway,
            scribeClient,
            Options.Create(new PortalOptions()),
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        var queued = Assert.Single(gateway.QueuedRequests);
        var forwarded = Assert.Single(queued.InlineAssets!);
        Assert.Equal("logo-header", forwarded.ContentId);
        Assert.Equal(cloudStorageFileId, forwarded.CloudStorageFileId);
    }

    private sealed class SucceedingScribeRenderClient(ScribeRenderedEmail render) : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(render));
    }

    private sealed class RecordingEmailDispatchGateway : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> QueuedRequests { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            QueuedRequests.Add(request);
            return Task.FromResult(
                new EmailDispatchResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    TaxVision.Notification.Domain.Notifications.NotificationDispatchAttemptStatus.Queued,
                    ProviderMessageId: null,
                    Error: null
                )
            );
        }
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
