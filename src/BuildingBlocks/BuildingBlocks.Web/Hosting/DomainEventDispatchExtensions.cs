using BuildingBlocks.Common;
using BuildingBlocks.Domain;
using BuildingBlocks.Web.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace BuildingBlocks.Web.Hosting;

/// <summary>
/// BB-1. Despacho de eventos de dominio según la guía (§4.5), opcional: no cambia nada hasta que el servicio
/// lo llama en su <c>UseWolverine</c>.
/// <list type="bullet">
/// <item>Al confirmar la transacción de un handler, Wolverine saca los eventos de toda
/// <see cref="AggregateRoot"/> y <see cref="TenantlessAggregateRoot"/> rastreada por el DbContext y los guarda
/// como mensajes en esa misma transacción.</item>
/// <item>Los eventos van a la cola local durable <see cref="DomainEventsQueue"/>: sobreviven a un reinicio.</item>
/// <item>Cada <c>XxxDomainEventHandler</c> devuelve el evento de integración; Wolverine lo manda en cascada por
/// el outbox.</item>
/// <item>El <c>X-Correlation-Id</c> de la petición viaja en el sobre y los handlers de dominio lo reciben en
/// <see cref="ICorrelationContext"/> (BB-9).</item>
/// </list>
/// Requisitos del servicio: <c>UseEntityFrameworkCoreTransactions()</c>, <c>AutoApplyTransactions()</c>, el
/// DbContext con <c>AddDbContext</c> normal y <c>Ignore(x =&gt; x.DomainEvents)</c> en el mapeo. Con
/// <c>AddDbContextWithWolverineIntegration</c> (Wolverine 6.14) el evento no queda guardado en el inbox durable.
/// Solo se despacha dentro de un handler de Wolverine o con <c>IDbContextOutbox.SaveChangesAndFlushMessagesAsync</c>;
/// un <c>SaveChangesAsync</c> suelto (controlador, job) deja los eventos en memoria y se pierden.
/// Al adoptarlo hay que retirar la publicación manual en el mismo cambio, o cada evento sale dos veces.
/// </summary>
public static class DomainEventDispatchExtensions
{
    public const string DomainEventsQueue = "domain-events";

    public static WolverineOptions UseTaxVisionDomainEvents(this WolverineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PublishDomainEventsFromEntityFrameworkCore<AggregateRoot, IDomainEvent>(root => root.DomainEvents);
        options.PublishDomainEventsFromEntityFrameworkCore<TenantlessAggregateRoot, IDomainEvent>(root =>
            root.DomainEvents
        );

        options.Publish(rule =>
        {
            rule.MessagesImplementing<IDomainEvent>();
            rule.ToLocalQueue(DomainEventsQueue);
        });
        options.LocalQueue(DomainEventsQueue).UseDurableInbox();

        options.Services.TryAddScoped<ICorrelationContext, CorrelationContext>();
        options.Services.AddScoped<RequestEnvelopeCorrelation>();
        options.Policies.AddMiddleware(
            typeof(DomainEventCorrelationMiddleware),
            chain => typeof(IDomainEvent).IsAssignableFrom(chain.MessageType)
        );

        return options;
    }
}
