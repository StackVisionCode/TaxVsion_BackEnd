namespace TaxVision.Connectors.Application.Accounts;

public static class ListTenantEmailAccountsHandler
{
    public static async Task<IReadOnlyList<TenantEmailAccountDto>> Handle(
        ListTenantEmailAccountsQuery query,
        ITenantEmailAccountRepository accountRepository,
        CancellationToken ct
    )
    {
        var accounts = await accountRepository.ListByTenantAsync(query.TenantId, ct);
        return accounts
            .Select(a => new TenantEmailAccountDto(
                a.Id,
                a.EmailAddress,
                a.ProviderCode.ToString(),
                a.DisplayName,
                a.Status.ToString(),
                a.ConnectedAtUtc,
                a.CreatedAtUtc
            ))
            .ToList();
    }
}
