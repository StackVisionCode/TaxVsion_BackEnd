using Wolverine;

namespace TaxVision.Notification.Infrastructure.Messaging;

/// <summary>
/// NOT-22. El bus que toca usar en este scope. Los colaboradores se resuelven por service location, y ahí
/// <see cref="IMessageBus"/> es otro contexto, fuera del outbox del handler. Sin handler (jobs, HTTP) es el del scope.
/// </summary>
public sealed class HandlerBus(IMessageBus scopeBus)
{
    private IMessageBus? _handlerBus;

    public IMessageBus Current => _handlerBus ?? scopeBus;

    public void Use(IMessageBus handlerBus) => _handlerBus = handlerBus;
}

/// <summary>Le da a <see cref="HandlerBus"/> el contexto del handler. Corre en el mismo scope que sus colaboradores.</summary>
public static class HandlerBusMiddleware
{
    public static void Before(IMessageContext context, HandlerBus bus) => bus.Use(context);
}
