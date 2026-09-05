using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.OnboardingPaymentOptions;
using TaxVision.PaymentApp.Domain.PaymentMethods;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers.PayPal;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Catalog;

public sealed class ConfiguredOnboardingPaymentMethodCatalog(
    IOptions<PaymentMethodCatalogOptions> options,
    IOptions<StripeOptions> stripeOptions,
    IOptions<PayPalOptions> payPalOptions,
    IOnboardingPaymentMethodOverrideRepository overrides
) : IOnboardingPaymentMethodCatalog
{
    private static readonly IReadOnlyList<OnboardingPaymentOption> DefaultOptions =
    [
        new(
            PaymentProviderCode.Stripe,
            PaymentMethodKind.Card,
            "Card",
            Enabled: true,
            Priority: 10,
            DisabledReason: null
        ),
        new(
            PaymentProviderCode.PayPal,
            PaymentMethodKind.Wallet,
            "PayPal",
            Enabled: false,
            Priority: 20,
            DisabledReason: "ProviderNotConfigured"
        ),
    ];

    public async Task<Result<IReadOnlyList<OnboardingPaymentOption>>> GetOptionsAsync(
        Guid planId,
        string billingCycle,
        string? currency = null,
        CancellationToken ct = default
    )
    {
        var configured = BuildConfiguredOptions(item => MatchesScope(item, planId, billingCycle, currency));
        if (configured.IsFailure)
            return Result.Failure<IReadOnlyList<OnboardingPaymentOption>>(configured.Error);

        return Result.Success(await ApplyOperationalStateAsync(configured.Value, ct));
    }

    public async Task<Result<IReadOnlyList<OnboardingPaymentOption>>> GetOperationalOptionsAsync(
        CancellationToken ct = default
    )
    {
        var configured = BuildConfiguredOptions(_ => true);
        if (configured.IsFailure)
            return Result.Failure<IReadOnlyList<OnboardingPaymentOption>>(configured.Error);

        var uniqueOptions = configured
            .Value.GroupBy(option => Key(option.Provider, option.Method.ToString()), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(option => option.Priority).First())
            .ToArray();

        return Result.Success(await ApplyOperationalStateAsync(uniqueOptions, ct));
    }

    public async Task<Result<OnboardingPaymentOption>> EnsureEnabledAsync(
        PaymentProviderCode provider,
        PaymentMethodKind method,
        Guid planId,
        string billingCycle,
        string? currency = null,
        CancellationToken ct = default
    )
    {
        var optionsResult = await GetOptionsAsync(planId, billingCycle, currency, ct);
        if (optionsResult.IsFailure)
            return Result.Failure<OnboardingPaymentOption>(optionsResult.Error);

        var option = optionsResult.Value.FirstOrDefault(x => x.Provider == provider && x.Method == method);
        if (option is null || !option.Enabled)
            return Result.Failure<OnboardingPaymentOption>(
                new Error(
                    "PaymentMethod.Disabled",
                    option?.DisabledReason ?? "The selected payment method is not available for onboarding."
                )
            );

        return Result.Success(option);
    }

    private Result<IReadOnlyList<OnboardingPaymentOption>> BuildConfiguredOptions(
        Func<ConfiguredOnboardingPaymentMethod, bool> matchesScope
    )
    {
        if (options.Value.Onboarding.Count == 0)
            return Result.Success(DefaultOptions);

        var configured = new List<OnboardingPaymentOption>();
        foreach (var item in options.Value.Onboarding)
        {
            var parsed = TryBuildOption(item);
            if (parsed.IsFailure)
                return Result.Failure<IReadOnlyList<OnboardingPaymentOption>>(parsed.Error);

            if (matchesScope(item))
                configured.Add(parsed.Value);
        }

        return Result.Success<IReadOnlyList<OnboardingPaymentOption>>(configured);
    }

    private async Task<IReadOnlyList<OnboardingPaymentOption>> ApplyOperationalStateAsync(
        IReadOnlyList<OnboardingPaymentOption> source,
        CancellationToken ct
    )
    {
        if (source.Count == 0)
            return [];

        var operationalOverrides = await overrides.ListAsync(ct);
        var byKey = operationalOverrides.ToDictionary(
            item => Key(item.ProviderCode, item.Method),
            StringComparer.OrdinalIgnoreCase
        );

        var effective = source.Select(option => ApplyProviderReadiness(ApplyOverride(option, byKey))).ToArray();

        return Sort(effective);
    }

    private static OnboardingPaymentOption ApplyOverride(
        OnboardingPaymentOption option,
        IReadOnlyDictionary<string, OnboardingPaymentMethodOverride> overridesByKey
    )
    {
        if (!overridesByKey.TryGetValue(Key(option.Provider, option.Method.ToString()), out var paymentOverride))
            return option;

        return option with
        {
            Enabled = paymentOverride.Enabled,
            DisabledReason = paymentOverride.Enabled ? null : paymentOverride.DisabledReason,
        };
    }

    private static Result<OnboardingPaymentOption> TryBuildOption(ConfiguredOnboardingPaymentMethod item)
    {
        if (!Enum.TryParse<PaymentProviderCode>(item.Provider, ignoreCase: true, out var provider))
            return Result.Failure<OnboardingPaymentOption>(
                new Error("PaymentMethodCatalog.ProviderInvalid", "Configured payment provider is invalid.")
            );

        if (!Enum.TryParse<PaymentMethodKind>(item.Method, ignoreCase: true, out var method))
            return Result.Failure<OnboardingPaymentOption>(
                new Error("PaymentMethodCatalog.MethodInvalid", "Configured payment method is invalid.")
            );

        var displayName = string.IsNullOrWhiteSpace(item.DisplayName)
            ? $"{provider} {method}"
            : item.DisplayName.Trim();

        return Result.Success(
            new OnboardingPaymentOption(
                provider,
                method,
                displayName,
                item.Enabled,
                item.Priority,
                string.IsNullOrWhiteSpace(item.DisabledReason) ? null : item.DisabledReason.Trim()
            )
        );
    }

    private OnboardingPaymentOption ApplyProviderReadiness(OnboardingPaymentOption option)
    {
        if (!option.Enabled)
            return option;

        return option.Provider switch
        {
            PaymentProviderCode.Stripe when !StripeIsConfigured() => DisableAsProviderNotConfigured(option),
            PaymentProviderCode.PayPal when !PayPalIsConfigured() => DisableAsProviderNotConfigured(option),
            _ => option,
        };
    }

    private bool StripeIsConfigured() =>
        !string.IsNullOrWhiteSpace(stripeOptions.Value.SecretKey)
        && !string.IsNullOrWhiteSpace(stripeOptions.Value.WebhookSecret);

    private bool PayPalIsConfigured() =>
        !string.IsNullOrWhiteSpace(payPalOptions.Value.ClientId)
        && !string.IsNullOrWhiteSpace(payPalOptions.Value.ClientSecret)
        && !string.IsNullOrWhiteSpace(payPalOptions.Value.WebhookId);

    private static OnboardingPaymentOption DisableAsProviderNotConfigured(OnboardingPaymentOption option) =>
        option with
        {
            Enabled = false,
            DisabledReason = option.DisabledReason ?? "ProviderNotConfigured",
        };

    private static IReadOnlyList<OnboardingPaymentOption> Sort(IReadOnlyList<OnboardingPaymentOption> source) =>
        source
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Provider.ToString(), StringComparer.Ordinal)
            .ThenBy(x => x.Method.ToString(), StringComparer.Ordinal)
            .ToArray();

    private static string Key(PaymentProviderCode provider, string method) => $"{provider}:{method}";

    private static bool MatchesScope(
        ConfiguredOnboardingPaymentMethod item,
        Guid planId,
        string billingCycle,
        string? currency
    ) =>
        MatchesPlan(item.PlanIds, planId)
        && MatchesAny(item.BillingCycles, billingCycle)
        && (string.IsNullOrWhiteSpace(currency) || MatchesAny(item.Currencies, currency));

    private static bool MatchesPlan(IReadOnlyCollection<string> planIds, Guid planId)
    {
        if (planIds.Count == 0)
            return true;

        return planIds.Any(value => Guid.TryParse(value, out var parsed) && parsed == planId);
    }

    private static bool MatchesAny(IReadOnlyCollection<string> configured, string value)
    {
        if (configured.Count == 0)
            return true;

        return configured.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
    }
}
