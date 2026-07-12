using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Analytics.Consumers;

public static class SignatureRequestCreatedAnalyticsConsumer
{
    public static async Task Handle(
        SignatureRequestCreatedIntegrationEvent evt,
        ISignatureAnalyticsRepository repository,
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
            var category = ParseCategory(evt.Category);
            var day = DateOnly.FromDateTime(evt.OccurredOn);
            var snapshot = await repository.GetOrCreateForDayAsync(evt.TenantId, day, category, ct);
            snapshot.IncrementCreated();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static SignatureCategory ParseCategory(string raw) =>
        Enum.TryParse<SignatureCategory>(raw, ignoreCase: true, out var parsed) ? parsed : SignatureCategory.Other;
}
