namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Notificación de cambio de Graph ya validada (clientState) y encolada en una cola local durable.
/// El webhook responde apenas la guarda: Graph espera la respuesta en ~3 s y, si el endpoint es lento
/// seguido, lo degrada y empieza a descartar notificaciones. El sync corre después, fuera de la request.
/// </summary>
/// <param name="CorrelationId">El de la request del webhook: la cola no hereda el contexto ambient.</param>
public sealed record GraphNotificationReceived(string SubscriptionId, string CorrelationId);
