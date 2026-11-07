using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Application.Abstractions;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Connectors.Infrastructure.Persistence;

/// <summary>Sobre el DbContext del scope: la acción sigue viendo las entidades que el handler ya cargó.</summary>
public sealed class OutboxUnit(IDbContextOutbox<ConnectorsDbContext> outbox) : IOutboxUnit
{
    public async Task RunAsync(Func<IMessageBus, CancellationToken, Task<bool>> work, CancellationToken ct = default)
    {
        await using var transaction = await outbox.DbContext.Database.BeginTransactionAsync(ct);
        if (await work(outbox, ct))
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
    }
}
