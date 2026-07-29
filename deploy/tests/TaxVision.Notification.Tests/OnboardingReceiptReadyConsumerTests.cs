using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Onboarding;

namespace TaxVision.Notification.Tests;

/// <summary>PayFlow (Fase 12) — cubre solo la parte persistente-y-testeable de
/// OnboardingEventConsumers.cs: OnboardingReceiptReadyConsumer escribe la proyección local que
/// resuelve la carrera con OnboardingRegistrationReadyConsumer (que sí depende de Scribe/gateway/
/// M2M — no se cubre acá, mismo criterio que el resto de AuthEventConsumers, nunca unit-testeados
/// en este servicio). Fakes locales de mano, sin Moq — mismo patrón que
/// AuthzPermissionsProjectionConsumersTests.cs.</summary>
public sealed class OnboardingReceiptReadyConsumerTests
{
    [Fact]
    public async Task Persists_the_lookup_when_none_exists()
    {
        var onboardingId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var repo = new FakeOnboardingReceiptLookupRepository();
        var uow = new NoOpUnitOfWork();

        var evt = new OnboardingReceiptReadyIntegrationEvent
        {
            OnboardingId = onboardingId,
            ReceiptFileId = fileId,
            ReceiptDownloadUrl = "https://auth.example.com/onboarding/receipts/" + fileId + "/download",
        };

        await OnboardingReceiptReadyConsumer.Handle(
            evt,
            repo,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        var stored = await repo.GetByOnboardingIdAsync(onboardingId);
        Assert.NotNull(stored);
        Assert.Equal(fileId, stored!.ReceiptFileId);
        Assert.Equal(evt.ReceiptDownloadUrl, stored.ReceiptDownloadUrl);
        Assert.Equal(1, uow.SaveCount);
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

        var evt = new OnboardingReceiptReadyIntegrationEvent
        {
            OnboardingId = onboardingId,
            ReceiptFileId = Guid.NewGuid(),
            ReceiptDownloadUrl = "https://a-different-url",
        };

        await OnboardingReceiptReadyConsumer.Handle(
            evt,
            repo,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        var stored = await repo.GetByOnboardingIdAsync(onboardingId);
        Assert.Same(existing, stored);
        Assert.Equal(0, uow.SaveCount);
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
}
