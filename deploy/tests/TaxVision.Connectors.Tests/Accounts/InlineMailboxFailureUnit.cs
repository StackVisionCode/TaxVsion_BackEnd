using BuildingBlocks.Persistence;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Watch;

namespace TaxVision.Connectors.Tests.Accounts;

/// <summary>La unidad sobre los repositorios en memoria de la prueba: corre la acción y guarda si confirma.</summary>
internal sealed class InlineMailboxFailureUnit(
    ITenantEmailAccountRepository accounts,
    IProviderWatchSubscriptionRepository subscriptions,
    IOAuthConnectionRepository connections,
    IUnitOfWork unitOfWork
) : IMailboxFailureUnit
{
    public async Task RunAsync(
        Func<MailboxFailureStores, CancellationToken, Task<bool>> work,
        CancellationToken ct = default
    )
    {
        if (await work(new MailboxFailureStores(accounts, subscriptions, connections), ct))
            await unitOfWork.SaveChangesAsync(ct);
    }
}
