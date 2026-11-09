using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace BuildingBlocks.Jobs;

/// <summary>
/// Lo que ve la acción de una unidad: el scope propio, su <typeparamref name="TDbContext"/> y el outbox
/// enlistado en la misma transacción. Los repositorios que se resuelvan de <see cref="Services"/>
/// comparten ese mismo DbContext.
/// </summary>
public sealed class JobUnitContext<TDbContext>
    where TDbContext : DbContext
{
    internal JobUnitContext(IServiceProvider services, IDbContextOutbox<TDbContext> outbox)
    {
        Services = services;
        Outbox = outbox;
    }

    public IServiceProvider Services { get; }

    public IDbContextOutbox<TDbContext> Outbox { get; }

    public TDbContext DbContext => Outbox.DbContext;

    /// <summary>Publica por el outbox: sale solo si la unidad confirma.</summary>
    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) =>
        Outbox.PublishAsync(message, options);
}
