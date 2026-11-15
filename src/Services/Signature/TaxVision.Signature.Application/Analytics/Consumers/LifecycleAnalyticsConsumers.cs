using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.TimeZones;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Analytics.Consumers;

/// <summary>
/// Consumers de eventos de lifecycle que actualizan el snapshot diario. Cada consumer
/// mantiene UNA sola responsabilidad (increment de UN contador). El nombre corresponde
/// directamente al evento; la categoría se obtiene cargando el aggregate para preservar
/// la agrupación correcta.
/// </summary>
public static class SignatureRequestCanceledAnalyticsConsumer
{
    public static async Task Handle(
        SignatureRequestCanceledIntegrationEvent evt,
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
            var day = CivilDate.Of(evt.CanceledAtUtc, await zones.ResolveAsync(evt.TenantId, ct));
            var snapshot = await analyticsRepository.GetOrCreateForDayAsync(
                evt.TenantId,
                day,
                SignatureCategoryParsing.ToSystemCategory(request.Category),
                ct
            );
            snapshot.IncrementCanceled();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}

public static class DocumentSignedAnalyticsConsumer
{
    public static async Task Handle(
        DocumentSignedIntegrationEvent evt,
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
            var day = CivilDate.Of(evt.SignedAtUtc, await zones.ResolveAsync(evt.TenantId, ct));
            var snapshot = await analyticsRepository.GetOrCreateForDayAsync(
                evt.TenantId,
                day,
                SignatureCategoryParsing.ToSystemCategory(request.Category),
                ct
            );
            snapshot.IncrementSignersSigned();
            if (evt.IsRequestCompleted)
                snapshot.IncrementCompleted();

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}

public static class SignerRejectedAnalyticsConsumer
{
    public static async Task Handle(
        SignerRejectedIntegrationEvent evt,
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
            var day = CivilDate.Of(evt.RejectedAtUtc, await zones.ResolveAsync(evt.TenantId, ct));
            var snapshot = await analyticsRepository.GetOrCreateForDayAsync(
                evt.TenantId,
                day,
                SignatureCategoryParsing.ToSystemCategory(request.Category),
                ct
            );
            snapshot.IncrementSignersRejected();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}

public static class SignatureRequestSealingCompletedAnalyticsConsumer
{
    public static async Task Handle(
        SignatureRequestSealingCompletedIntegrationEvent evt,
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
            // Un re-sellado (tras un rechazo de CloudStorage) vuelve a publicar el evento: cuenta una sola vez.
            if (!await analyticsRepository.TryMarkSealedAsync(evt.TenantId, evt.SignatureRequestId, ct))
                return;
            // P21: el día de la métrica es el de la oficina (solo hacia adelante).
            var day = CivilDate.Of(evt.SealedAtUtc, await zones.ResolveAsync(evt.TenantId, ct));
            var snapshot = await analyticsRepository.GetOrCreateForDayAsync(
                evt.TenantId,
                day,
                SignatureCategoryParsing.ToSystemCategory(request.Category),
                ct
            );
            snapshot.IncrementSealed();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
