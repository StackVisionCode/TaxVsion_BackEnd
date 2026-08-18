using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Onboarding;

namespace TaxVision.Notification.Tests;

/// <summary>PayFlow (Fase 12 + fix del día que agregó el segundo envío) — cubre las dos ramas del
/// OnboardingReceiptReadyConsumer: (1) primera llegada persiste la proyección Y encola el email de
/// seguimiento con el link de descarga, (2) redelivery encuentra la proyección ya persistida y sale
/// sin encolar (guard de idempotencia). Fakes locales de mano, sin Moq — mismo patrón que
/// AuthzPermissionsProjectionConsumersTests.cs.</summary>
public sealed class OnboardingReceiptReadyConsumerTests
{
    private static readonly PortalOptions Portal = new() { ProductName = "TaxVision" };

    [Fact]
    public async Task Persists_the_lookup_and_queues_the_follow_up_email_when_none_exists()
    {
        var onboardingId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var repo = new FakeOnboardingReceiptLookupRepository();
        var uow = new NoOpUnitOfWork();
        var gateway = new RecordingEmailDispatchGateway();
        var scribeClient = new FakeScribeRenderClient();

        var evt = new OnboardingReceiptReadyIntegrationEvent
        {
            OnboardingId = onboardingId,
            ReceiptFileId = fileId,
            ReceiptDownloadUrl = "https://auth.example.com/onboarding/receipts/" + fileId + "/download",
            Email = "buyer@example.com",
            FirstName = "Ada",
        };

        await OnboardingReceiptReadyConsumer.Handle(
            evt,
            repo,
            gateway,
            scribeClient,
            Options.Create(Portal),
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        var stored = await repo.GetByOnboardingIdAsync(onboardingId);
        Assert.NotNull(stored);
        Assert.Equal(fileId, stored!.ReceiptFileId);
        Assert.Equal(evt.ReceiptDownloadUrl, stored.ReceiptDownloadUrl);
        Assert.Equal(1, uow.SaveCount);

        var queued = Assert.Single(gateway.Queued);
        Assert.Equal("buyer@example.com", queued.To);
        Assert.Equal("onboarding.receipt_ready", queued.TemplateKey);
        Assert.Equal(evt.EventId, queued.RelatedEventId);
    }

    [Fact]
    public async Task Is_idempotent_when_the_lookup_already_exists()
    {
        var onboardingId = Guid.NewGuid();
        var existing = OnboardingReceiptLookup.Create(
            onboardingId,
            Guid.NewGuid(),
            "https://existing",
            DateTime.UtcNow
        );
        var repo = new FakeOnboardingReceiptLookupRepository(existing);
        var uow = new NoOpUnitOfWork();
        var gateway = new RecordingEmailDispatchGateway();
        var scribeClient = new FakeScribeRenderClient();

        var evt = new OnboardingReceiptReadyIntegrationEvent
        {
            OnboardingId = onboardingId,
            ReceiptFileId = Guid.NewGuid(),
            ReceiptDownloadUrl = "https://a-different-url",
            Email = "buyer@example.com",
            FirstName = "Ada",
        };

        await OnboardingReceiptReadyConsumer.Handle(
            evt,
            repo,
            gateway,
            scribeClient,
            Options.Create(Portal),
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        var stored = await repo.GetByOnboardingIdAsync(onboardingId);
        Assert.Same(existing, stored);
        Assert.Equal(0, uow.SaveCount);
        Assert.Empty(gateway.Queued);
    }

    private sealed class FakeOnboardingReceiptLookupRepository : IOnboardingReceiptLookupRepository
    {
        private readonly Dictionary<Guid, OnboardingReceiptLookup> _byOnboardingId = new();

        public FakeOnboardingReceiptLookupRepository(params OnboardingReceiptLookup[] seed)
        {
            foreach (var lookup in seed)
                _byOnboardingId[lookup.OnboardingId] = lookup;
        }

        public Task<OnboardingReceiptLookup?> GetByOnboardingIdAsync(
            Guid onboardingId,
            CancellationToken ct = default
        ) => Task.FromResult(_byOnboardingId.GetValueOrDefault(onboardingId));

        public Task AddAsync(OnboardingReceiptLookup lookup, CancellationToken ct = default)
        {
            _byOnboardingId[lookup.OnboardingId] = lookup;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
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
                Result.Success(
                    new ScribeRenderedEmail("Tu recibo ya está disponible", "<p>Descarga tu recibo</p>", null)
                )
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
