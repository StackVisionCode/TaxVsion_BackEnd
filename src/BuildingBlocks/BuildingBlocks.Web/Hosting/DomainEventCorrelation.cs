using BuildingBlocks.Common;
using Wolverine;

namespace BuildingBlocks.Web.Hosting;

/// <summary>
/// BB-9. Parte de <c>UseTaxVisionDomainEvents()</c>: el handler de un evento de dominio corre en un scope nuevo,
/// así que <see cref="ICorrelationContext"/> llega vacío. Se restaura desde el sobre. Debe pedir la interfaz, igual
/// que los handlers, para compartir la misma instancia.
/// </summary>
public static class DomainEventCorrelationMiddleware
{
    public static void Before(Envelope envelope, ICorrelationContext correlation)
    {
        if (string.IsNullOrEmpty(correlation.CorrelationId) && !string.IsNullOrEmpty(envelope.CorrelationId))
            correlation.Set(envelope.CorrelationId);
    }
}

/// <summary>
/// BB-9. Sin esto, el sobre de lo enviado desde una petición lleva el id de la traza y no el
/// <c>X-Correlation-Id</c>. Solo existe en los servicios que llaman <c>UseTaxVisionDomainEvents()</c>.
/// </summary>
internal sealed class RequestEnvelopeCorrelation(IMessageContext bus)
{
    public void Apply(string correlationId) => bus.CorrelationId = correlationId;
}
