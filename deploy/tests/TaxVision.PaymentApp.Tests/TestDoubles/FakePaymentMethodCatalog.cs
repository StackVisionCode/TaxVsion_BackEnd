using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.OnboardingPaymentOptions;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.TestDoubles;

/// <summary>Catálogo operativo fijo para los checkouts de una oficina activa.</summary>
public sealed class FakePaymentMethodCatalog : IOnboardingPaymentMethodCatalog
{
    private readonly Result<IReadOnlyList<OnboardingPaymentOption>> _options;

    public FakePaymentMethodCatalog(params OnboardingPaymentOption[] options) =>
        _options = Result.Success<IReadOnlyList<OnboardingPaymentOption>>(options);

    public FakePaymentMethodCatalog(Error error) =>
        _options = Result.Failure<IReadOnlyList<OnboardingPaymentOption>>(error);

    public static OnboardingPaymentOption Option(
        PaymentProviderCode provider,
        PaymentMethodKind method,
        bool enabled = true,
        string? disabledReason = null
    ) => new(provider, method, method.ToString(), enabled, 10, enabled ? null : disabledReason);

    /// <summary>Tarjeta y PayPal encendidos.</summary>
    public static FakePaymentMethodCatalog AllEnabled() =>
        new(
            Option(PaymentProviderCode.Stripe, PaymentMethodKind.Card),
            Option(PaymentProviderCode.PayPal, PaymentMethodKind.Wallet)
        );

    public Task<Result<IReadOnlyList<OnboardingPaymentOption>>> GetOptionsAsync(
        Guid planId,
        string billingCycle,
        string? currency = null,
        CancellationToken ct = default
    ) => Task.FromResult(_options);

    public Task<Result<IReadOnlyList<OnboardingPaymentOption>>> GetOperationalOptionsAsync(
        CancellationToken ct = default
    ) => Task.FromResult(_options);

    public Task<Result<OnboardingPaymentOption>> EnsureEnabledAsync(
        PaymentProviderCode provider,
        PaymentMethodKind method,
        Guid planId,
        string billingCycle,
        string? currency = null,
        CancellationToken ct = default
    ) => throw new NotSupportedException("Onboarding has its own catalog fake.");
}
