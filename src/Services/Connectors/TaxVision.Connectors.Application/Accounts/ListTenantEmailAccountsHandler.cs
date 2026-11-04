using TaxVision.Connectors.Application.OAuth;

namespace TaxVision.Connectors.Application.Accounts;

public static class ListTenantEmailAccountsHandler
{
    public static async Task<IReadOnlyList<TenantEmailAccountDto>> Handle(
        ListTenantEmailAccountsQuery query,
        ITenantEmailAccountRepository accountRepository,
        IOAuthConnectionRepository connectionRepository,
        IOAuthProviderClientFactory clientFactory,
        CancellationToken ct
    )
    {
        var accounts = await accountRepository.ListVisibleAsync(
            query.TenantId,
            query.CallerUserId,
            query.CanSeeOffice,
            ct
        );
        return await TenantEmailAccountDtoFactory.CreateAsync(accounts, connectionRepository, clientFactory, ct);
    }
}
