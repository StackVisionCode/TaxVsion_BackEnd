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
        // Un buzón personal solo lo ve su dueño; el de oficina, quien tenga office.read. Otro tenant,
        // el personal de un colega, o la oficina sin permiso → NotFound (anti-enumeración).
        var hidden =
            account.TenantId != query.TenantId
            || (account.IsOffice && !query.CanSeeOffice)
            || (!account.IsOffice && account.OwnerUserId != query.CallerUserId);
        if (hidden)
            return Result.Failure<TenantEmailAccountDto>(
                new Error("TenantEmailAccount.NotFound", "The email account was not found.")
            );

        return Result.Success(
            new TenantEmailAccountDto(
                account.Id,
                account.EmailAddress,
                account.ProviderCode.ToString(),
                account.DisplayName,
                account.Status.ToString(),
                account.ConnectedAtUtc,
                account.CreatedAtUtc,
                account.OwnerUserId,
                account.IsOffice
            )
        );
    }
}
