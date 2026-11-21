using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Billing.Infrastructure.Messaging;

/// <summary>Scope y outbox propios por página: un fallo no arrastra a las páginas ya confirmadas.</summary>
public sealed class OutboxIntegrationEventPagePublisher(IServiceScopeFactory scopeFactory)
    : IIntegrationEventPagePublisher
{
    public async Task PublishPageAsync(IReadOnlyList<IIntegrationEvent> events, CancellationToken ct = default)
    {
        using var unit = scopeFactory.CreateScope();
        var outbox = unit.ServiceProvider.GetRequiredService<IDbContextOutbox<BillingDbContext>>();
        foreach (var integrationEvent in events)
            await outbox.PublishAsync(integrationEvent);
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
    }
}
