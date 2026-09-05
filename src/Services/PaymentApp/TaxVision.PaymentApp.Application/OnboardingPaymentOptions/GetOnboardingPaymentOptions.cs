using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.OnboardingPaymentOptions;

public sealed record GetOnboardingPaymentOptionsQuery(Guid PlanId, string BillingCycle, string? Currency = null);

public sealed record OnboardingPaymentOptionsResponse(IReadOnlyList<OnboardingPaymentOption> Options);

public sealed record OnboardingPaymentOption(
    PaymentProviderCode Provider,
    PaymentMethodKind Method,
    string DisplayName,
    bool Enabled,
    int Priority,
    string? DisabledReason
);

public static class GetOnboardingPaymentOptionsHandler
{
    public static async Task<Result<OnboardingPaymentOptionsResponse>> Handle(
        GetOnboardingPaymentOptionsQuery query,
        IOnboardingPaymentMethodCatalog catalog,
        CancellationToken ct
    )
    {
        if (query.PlanId == Guid.Empty)
            return Result.Failure<OnboardingPaymentOptionsResponse>(
                new Error("PaymentMethodCatalog.PlanIdRequired", "Plan id is required.")
            );

        if (string.IsNullOrWhiteSpace(query.BillingCycle))
            return Result.Failure<OnboardingPaymentOptionsResponse>(
                new Error("PaymentMethodCatalog.BillingCycleRequired", "Billing cycle is required.")
            );

        var options = await catalog.GetOptionsAsync(query.PlanId, query.BillingCycle, query.Currency, ct);
        return options.IsSuccess
            ? Result.Success(new OnboardingPaymentOptionsResponse(options.Value))
            : Result.Failure<OnboardingPaymentOptionsResponse>(options.Error);
    }
}
