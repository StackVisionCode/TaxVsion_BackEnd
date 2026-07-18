namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Port propio de Notification para publicar integration events, desacoplado de <c>Wolverine.IMessageBus</c>.
/// Simplifica los tests y permite un fake trivial. La implementación por defecto
/// (<c>WolverineIntegrationEventPublisher</c> en Infrastructure) delega en <c>IMessageBus.PublishAsync</c>.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync<T>(T message, CancellationToken ct = default)
        where T : class;
}
