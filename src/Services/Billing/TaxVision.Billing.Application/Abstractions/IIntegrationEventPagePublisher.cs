using BuildingBlocks.Messaging;

namespace TaxVision.Billing.Application.Abstractions;

/// <summary>Publica una página de eventos en su propia transacción con outbox: salen todos o ninguno.</summary>
public interface IIntegrationEventPagePublisher
{
    Task PublishPageAsync(IReadOnlyList<IIntegrationEvent> events, CancellationToken ct = default);
}
