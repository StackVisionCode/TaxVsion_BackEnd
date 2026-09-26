using BuildingBlocks.Results;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Dueño/tipo de una cuenta del tenant — usado por los endpoints de gestión (disconnect/reauth) para autorizar antes de despachar el comando real. Tenant-scoped, sin restricción de dueño (a diferencia de <see cref="GetTenantEmailAccountQuery"/>): el admin necesita poder resolver cualquier cuenta del tenant.</summary>
public sealed record ResolveAccountOwnershipQuery(Guid TenantId, Guid AccountId);

public sealed record AccountOwnership(Guid AccountId, bool IsOffice, Guid? OwnerUserId);

public static class ResolveAccountOwnershipHandler
{
    public static async Task<Result<AccountOwnership>> Handle(
        ResolveAccountOwnershipQuery query,
        ITenantEmailAccountRepository accountRepository,
        CancellationToken ct
    )
    {
        var accountResult = await accountRepository.GetByIdAsync(query.AccountId, ct);
        if (accountResult.IsFailure || accountResult.Value.TenantId != query.TenantId)
            return Result.Failure<AccountOwnership>(
                new Error("TenantEmailAccount.NotFound", "The email account was not found.")
            );

        var account = accountResult.Value;
        return Result.Success(new AccountOwnership(account.Id, account.IsOffice, account.OwnerUserId));
    }
}
