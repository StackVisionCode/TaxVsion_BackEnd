using BuildingBlocks.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Infrastructure.Persistence;

namespace TaxVision.Connectors.Infrastructure.Accounts;

/// <summary>
/// Dentro de un handler ya hay transacción: se usa esa, porque abrir otra chocaría con lo que el handler tenga sin
/// confirmar sobre la misma cuenta. Sin transacción (los jobs) corre una <see cref="TransactionalJobUnit"/>.
/// </summary>
public sealed class MailboxFailureUnit(
    ConnectorsDbContext dbContext,
    ITenantEmailAccountRepository accounts,
    IProviderWatchSubscriptionRepository subscriptions,
    IServiceScopeFactory scopeFactory,
    ILogger<MailboxFailureUnit> logger
) : IMailboxFailureUnit
{
    public async Task RunAsync(
        Func<MailboxFailureStores, CancellationToken, Task<bool>> work,
        CancellationToken ct = default
    )
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            if (await work(new MailboxFailureStores(accounts, subscriptions), ct))
                await dbContext.SaveChangesAsync(ct);
            return;
        }

        await TransactionalJobUnit.RunAsync<ConnectorsDbContext>(
            scopeFactory,
            (unit, token) =>
                work(
                    new MailboxFailureStores(
                        unit.Services.GetRequiredService<ITenantEmailAccountRepository>(),
                        unit.Services.GetRequiredService<IProviderWatchSubscriptionRepository>()
                    ),
                    token
                ),
            logger,
            ct
        );
    }
}
