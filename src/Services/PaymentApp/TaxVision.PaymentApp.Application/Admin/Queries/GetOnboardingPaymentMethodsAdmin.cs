using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;

namespace TaxVision.PaymentApp.Application.Admin.Queries;

public sealed record GetOnboardingPaymentMethodsAdminQuery;

public sealed record OnboardingPaymentMethodAdminResponse(
    string Provider,
    string Method,
    string DisplayName,
    bool Enabled,
    int Priority,
    string? DisabledReason,
    bool HasOverride,
    DateTime? UpdatedAtUtc,
    Guid? UpdatedByUserId
);

public static class GetOnboardingPaymentMethodsAdminHandler
{
    public static async Task<Result<IReadOnlyList<OnboardingPaymentMethodAdminResponse>>> Handle(
        GetOnboardingPaymentMethodsAdminQuery query,
        IOnboardingPaymentMethodCatalog catalog,
        IOnboardingPaymentMethodOverrideRepository overrides,
        CancellationToken ct
    )
    {
        var options = await catalog.GetOperationalOptionsAsync(ct);
        if (options.IsFailure)
            return Result.Failure<IReadOnlyList<OnboardingPaymentMethodAdminResponse>>(options.Error);

        var operationalOverrides = await overrides.ListAsync(ct);
        var overridesByKey = operationalOverrides.ToDictionary(
            item => Key(item.ProviderCode.ToString(), item.Method),
            StringComparer.OrdinalIgnoreCase
        );

        return Result.Success<IReadOnlyList<OnboardingPaymentMethodAdminResponse>>(
            options
                .Value.Select(option =>
                {
                    overridesByKey.TryGetValue(
                        Key(option.Provider.ToString(), option.Method.ToString()),
                        out var paymentOverride
                    );
                    return new OnboardingPaymentMethodAdminResponse(
                        option.Provider.ToString(),
                        option.Method.ToString(),
                        option.DisplayName,
                        option.Enabled,
                        option.Priority,
                        option.DisabledReason,
                        paymentOverride is not null,
                        paymentOverride?.UpdatedAtUtc,
                        paymentOverride?.UpdatedByUserId
                    );
                })
                .ToArray()
        );
    }

    private static string Key(string provider, string method) => $"{provider}:{method}";
}
