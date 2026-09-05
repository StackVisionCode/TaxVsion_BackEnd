using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.PaymentOptions;

public sealed record GetOnboardingPaymentOptionsQuery(Guid PlanId, string BillingCycle, string? Currency = null);

public sealed record OnboardingPaymentOptionsResponse(IReadOnlyList<OnboardingPaymentOption> Options);

public sealed record OnboardingPaymentOption(
    string Provider,
    string Method,
    string DisplayName,
    bool Enabled,
    int Priority,
    string? DisabledReason
);

public static class GetOnboardingPaymentOptionsHandler
{
    public static async Task<Result<OnboardingPaymentOptionsResponse>> Handle(
        GetOnboardingPaymentOptionsQuery query,
        IPaymentAppOnboardingClient paymentApp,
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

        var result = await paymentApp.GetPaymentOptionsAsync(
            new PaymentAppPaymentOptionsRequest(query.PlanId, query.BillingCycle, query.Currency),
            ct
        );
        if (result.IsFailure)
            return Result.Failure<OnboardingPaymentOptionsResponse>(result.Error);

        return Result.Success(
            new OnboardingPaymentOptionsResponse(
                result
                    .Value.Options.Select(option => new OnboardingPaymentOption(
                        option.Provider,
                        option.Method,
                        option.DisplayName,
                        option.Enabled,
                        option.Priority,
                        option.DisabledReason
                    ))
                    .ToArray()
            )
        );
    }
}
