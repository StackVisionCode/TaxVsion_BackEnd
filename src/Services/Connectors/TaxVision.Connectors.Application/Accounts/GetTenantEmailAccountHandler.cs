using BuildingBlocks.Results;

namespace TaxVision.Connectors.Application.Accounts;

public static class GetTenantEmailAccountHandler
{
    public static async Task<Result<TenantEmailAccountDto>> Handle(
        GetTenantEmailAccountQuery query,
        ITenantEmailAccountRepository accountRepository,
        CancellationToken ct
    )
    {
        var accountResult = await accountRepository.GetByIdAsync(query.AccountId, ct);
        if (accountResult.IsFailure)
            return Result.Failure<TenantEmailAccountDto>(accountResult.Error);

        var account = accountResult.Value;
        if (account.TenantId != query.TenantId)
            return Result.Failure<TenantEmailAccountDto>(
                new Error("GetTenantEmailAccountHandler.Forbidden", "Account does not belong to the caller's tenant.")
            );

        return Result.Success(
            new TenantEmailAccountDto(
                account.Id,
                account.EmailAddress,
                account.ProviderCode.ToString(),
                account.DisplayName,
                account.Status.ToString(),
                account.ConnectedAtUtc,
                account.CreatedAtUtc
            )
        );
    }
}
