using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Analytics.Consumers;

public static class SignatureRequestSentAnalyticsConsumer
{
    public static async Task Handle(
        SignatureRequestSentIntegrationEvent evt,
        ISignatureRequestRepository requestRepository,
        ISignatureAnalyticsRepository analyticsRepository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;
        using (correlation.Push(correlationId))
        {
            var request = await requestRepository.GetByIdAsync(evt.TenantId, evt.SignatureRequestId, ct);
            if (request is null)
                return;

            var day = DateOnly.FromDateTime(evt.SentAtUtc);
            var snapshot = await analyticsRepository.GetOrCreateForDayAsync(evt.TenantId, day, request.Category, ct);
            snapshot.IncrementSent();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
