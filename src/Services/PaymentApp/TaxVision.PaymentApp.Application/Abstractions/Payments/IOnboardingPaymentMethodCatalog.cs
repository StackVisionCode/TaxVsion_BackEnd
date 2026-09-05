using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.OnboardingPaymentOptions;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Abstractions.Payments;

/// <summary>
/// Fuente autoritativa de metodos de pago visibles/permitidos durante onboarding. El frontend
/// puede ocultar opciones OFF, pero PaymentApp sigue validando cada checkout server-side.
/// </summary>
public interface IOnboardingPaymentMethodCatalog
{
    Task<Result<IReadOnlyList<OnboardingPaymentOption>>> GetOptionsAsync(
        Guid planId,
        string billingCycle,
        string? currency = null,
        CancellationToken ct = default
    );

    Task<Result<IReadOnlyList<OnboardingPaymentOption>>> GetOperationalOptionsAsync(CancellationToken ct = default);

    Task<Result<OnboardingPaymentOption>> EnsureEnabledAsync(
        PaymentProviderCode provider,
        PaymentMethodKind method,
        Guid planId,
        string billingCycle,
        string? currency = null,
        CancellationToken ct = default
    );
}
