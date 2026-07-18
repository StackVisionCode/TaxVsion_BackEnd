using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantConnect.Queries;

public static class GetTenantConnectAccountHandler
{
    public static async Task<Result<TenantConnectAccountResponse>> Handle(
        GetTenantConnectAccountQuery query, ITenantConnectAccountRepository connectAccounts, CancellationToken ct)
    {
        var account = await connectAccounts.GetByTenantAndProviderAsync(query.TenantId, PaymentProviderCode.Stripe, ct);
        if (account is null)
            return Result.Failure<TenantConnectAccountResponse>(new Error("TenantConnectAccount.NotFound", "TenantConnectAccount does not exist."));

        return Result.Success(new TenantConnectAccountResponse(
            account.Id,
            account.AccountType.ToString(),
            account.Status.ToString(),
            account.OnboardingStep.ToString(),
            account.CanCharge,
            account.CanReceivePayouts,
            account.RequirementsCurrentlyDue));
    }
}
