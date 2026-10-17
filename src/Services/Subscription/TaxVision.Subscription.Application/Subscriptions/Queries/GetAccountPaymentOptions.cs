using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.Queries;

/// <summary>
/// Query del landing para pintar el picker "Choose how to pay" antes de redirigir al proveedor en
/// cualquier compra manual (add-on, seats, renewal, plan change). Delega en PaymentApp — fuente única
/// del catálogo — vía M2M.
/// </summary>
public sealed record GetAccountPaymentOptionsQuery(Guid TenantId);

public sealed record AccountPaymentOptionView(
    string Provider,
    string Method,
    string DisplayName,
    bool Enabled,
    int Priority,
    string? DisabledReason
);

public sealed record AccountPaymentOptionsResponse(IReadOnlyList<AccountPaymentOptionView> Options);

public static class GetAccountPaymentOptionsHandler
{
    public static async Task<Result<AccountPaymentOptionsResponse>> Handle(
        GetAccountPaymentOptionsQuery query,
        IAccountPaymentOptionsClient client,
        CancellationToken ct
    )
    {
        var result = await client.GetOptionsAsync(query.TenantId, ct);
        if (result.IsFailure)
            return Result.Failure<AccountPaymentOptionsResponse>(result.Error);

        var views = result
            .Value.Options.Select(option => new AccountPaymentOptionView(
                option.Provider,
                option.Method,
                option.DisplayName,
                option.Enabled,
                option.Priority,
                option.DisabledReason
            ))
            .ToList();
        return Result.Success(new AccountPaymentOptionsResponse(views));
    }
}
