using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Analytics.Consumers;
using TaxVision.Signature.Domain.Analytics;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Tests.Delivery;
using TaxVision.Signature.Tests.Projections;

namespace TaxVision.Signature.Tests.Application;

/// <summary>El sellado de una solicitud suma una sola vez en las métricas, aunque se vuelva a sellar.</summary>
public sealed class SignatureRequestSealingCompletedAnalyticsConsumerTests
{
    [Fact]
    public async Task A_first_sealing_counts_once()
    {
        var kit = new DeliveryTestKit().Seal();
        var analytics = new InMemoryAnalytics();

        await Handle(kit, analytics, DateTime.UtcNow);

        Assert.Equal(1, analytics.TotalSealed);
    }

    [Fact]
    public async Task A_reseal_of_the_same_request_does_not_count_again()
    {
        var kit = new DeliveryTestKit().Seal();
        var analytics = new InMemoryAnalytics();
        var firstSealedAt = DateTime.UtcNow;

        await Handle(kit, analytics, firstSealedAt);
        await Handle(kit, analytics, firstSealedAt.AddDays(1));

        Assert.Equal(1, analytics.TotalSealed);
    }

    [Fact]
    public async Task Each_request_counts_on_its_own()
    {
        var analytics = new InMemoryAnalytics();

        await Handle(new DeliveryTestKit().Seal(), analytics, DateTime.UtcNow);
        await Handle(new DeliveryTestKit().Seal(), analytics, DateTime.UtcNow);

        Assert.Equal(2, analytics.TotalSealed);
    }

    private static Task Handle(DeliveryTestKit kit, InMemoryAnalytics analytics, DateTime sealedAtUtc) =>
        SignatureRequestSealingCompletedAnalyticsConsumer.Handle(
            new SignatureRequestSealingCompletedIntegrationEvent
            {
                TenantId = kit.Request.TenantId,
                SignatureRequestId = kit.Request.Id,
                CreatedByUserId = kit.Request.CreatedByUserId,
                DocumentCount = kit.Request.Documents.Count,
                SealedAtUtc = sealedAtUtc,
            },
            new SingleRequestRepository(kit.Request),
            analytics,
            new SavingUnitOfWork(),
            new FixedCorrelation(),
            CancellationToken.None
        );

    private sealed class InMemoryAnalytics : ISignatureAnalyticsRepository
    {
        private readonly List<SignatureAnalyticsSnapshot> _snapshots = [];
        private readonly HashSet<(Guid TenantId, Guid RequestId)> _sealed = [];

        public int TotalSealed => _snapshots.Sum(snapshot => snapshot.RequestsSealed);

        public Task<SignatureAnalyticsSnapshot> GetOrCreateForDayAsync(
            Guid tenantId,
            DateOnly day,
            SignatureCategory category,
            CancellationToken ct = default
        )
        {
            var snapshot = _snapshots.SingleOrDefault(item =>
                item.TenantId == tenantId && item.Day == day && item.Category == category
            );
            if (snapshot is null)
            {
                snapshot = SignatureAnalyticsSnapshot.CreateEmpty(tenantId, day, category);
                _snapshots.Add(snapshot);
            }
            return Task.FromResult(snapshot);
        }

        public Task<bool> TryMarkSealedAsync(Guid tenantId, Guid signatureRequestId, CancellationToken ct = default) =>
            Task.FromResult(_sealed.Add((tenantId, signatureRequestId)));
    }
}
