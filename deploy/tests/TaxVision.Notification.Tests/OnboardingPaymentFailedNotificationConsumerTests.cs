using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests;

/// <summary>Cubre el aviso de pago fallido: renderiza la plantilla onboarding.payment_failed y encola
/// el email al comprador con el link de reintento. Fakes locales de mano, mismo patrón que
/// OnboardingReceiptReadyConsumerTests.</summary>
public sealed class OnboardingPaymentFailedNotificationConsumerTests
{
    private static readonly PortalOptions Portal = new() { ProductName = "TaxProffice" };

    [Fact]
    public async Task Renders_and_queues_the_payment_failed_email()
    {
        var gateway = new RecordingEmailDispatchGateway();
        var scribeClient = new FakeScribeRenderClient();

        var evt = new OnboardingPaymentFailedNotificationRequestedIntegrationEvent
        {
            OnboardingId = Guid.NewGuid(),
            Email = "buyer@example.com",
            FirstName = "Ada",
            PlanName = "Pro",
            FailureReason = "Your card was declined.",
            RetryUrl = "https://app.example.com/register?plan=p1&cycle=Monthly",
        };

        await OnboardingPaymentFailedNotificationConsumer.Handle(
            evt,
            gateway,
            scribeClient,
            Options.Create(Portal),
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        var queued = Assert.Single(gateway.Queued);
        Assert.Equal("buyer@example.com", queued.To);
        Assert.Equal("onboarding.payment_failed", queued.TemplateKey);
        Assert.Equal(evt.EventId, queued.RelatedEventId);
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new NoOpDisposable();
        }

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class FakeScribeRenderClient : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Success(new ScribeRenderedEmail("Tu pago no se procesó", "<p>Inténtalo de nuevo</p>", null))
            );
    }

    private sealed class RecordingEmailDispatchGateway : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> Queued { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            Queued.Add(request);
            return Task.FromResult(
                new EmailDispatchResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    NotificationDispatchAttemptStatus.Queued,
                    null,
                    null
                )
            );
        }
    }
}
