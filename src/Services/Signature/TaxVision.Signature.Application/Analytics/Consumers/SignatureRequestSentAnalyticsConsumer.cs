using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.TimeZones;
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
        IOfficeTimeZone zones,
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

            // P21: el día de la métrica es el de la oficina (solo hacia adelante).
            var day = CivilDate.Of(evt.SentAtUtc, await zones.ResolveAsync(evt.TenantId, ct));
            var snapshot = await analyticsRepository.GetOrCreateForDayAsync(
                evt.TenantId,
                day,
                SignatureCategoryParsing.ToSystemCategory(request.Category),
                ct
            );
            snapshot.IncrementSent();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
