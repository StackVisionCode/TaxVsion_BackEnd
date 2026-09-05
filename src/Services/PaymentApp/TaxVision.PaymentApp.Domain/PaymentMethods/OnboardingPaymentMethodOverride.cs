using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Domain.PaymentMethods;

/// <summary>
/// Switch operativo global para metodos de pago del onboarding. No es tenant-owned:
/// controla la plataforma antes de que exista el tenant.
/// </summary>
public sealed class OnboardingPaymentMethodOverride : BaseEntity
{
    private const int MethodMaxLength = 50;
    private const int DisabledReasonMaxLength = 200;

    public PaymentProviderCode ProviderCode { get; private set; }
    public string Method { get; private set; } = default!;
    public bool Enabled { get; private set; }
    public string? DisabledReason { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }

    private OnboardingPaymentMethodOverride() { }

    public static Result<OnboardingPaymentMethodOverride> Create(
        PaymentProviderCode providerCode,
        string method,
        bool enabled,
        string? disabledReason,
        Guid updatedByUserId,
        DateTime nowUtc
    )
    {
        var normalized = Normalize(providerCode, method, enabled, disabledReason, updatedByUserId);
        if (normalized.IsFailure)
            return Result.Failure<OnboardingPaymentMethodOverride>(normalized.Error);

        return Result.Success(
            new OnboardingPaymentMethodOverride
            {
                ProviderCode = providerCode,
                Method = normalized.Value.Method,
                Enabled = enabled,
                DisabledReason = normalized.Value.DisabledReason,
                UpdatedAtUtc = nowUtc,
                UpdatedByUserId = updatedByUserId,
            }
        );
    }

    public Result UpdateAvailability(bool enabled, string? disabledReason, Guid updatedByUserId, DateTime nowUtc)
    {
        var normalized = Normalize(ProviderCode, Method, enabled, disabledReason, updatedByUserId);
        if (normalized.IsFailure)
            return normalized;

        Method = normalized.Value.Method;
        Enabled = enabled;
        DisabledReason = normalized.Value.DisabledReason;
        UpdatedAtUtc = nowUtc;
        UpdatedByUserId = updatedByUserId;
        return Result.Success();
    }

    private static Result<(string Method, string? DisabledReason)> Normalize(
        PaymentProviderCode providerCode,
        string method,
        bool enabled,
        string? disabledReason,
        Guid updatedByUserId
    )
    {
        if (!Enum.IsDefined(providerCode))
            return Result.Failure<(string, string?)>(
                new Error("PaymentMethodOverride.ProviderInvalid", "Payment provider is invalid.")
            );

        if (string.IsNullOrWhiteSpace(method))
            return Result.Failure<(string, string?)>(
                new Error("PaymentMethodOverride.MethodRequired", "Payment method is required.")
            );

        if (updatedByUserId == Guid.Empty)
            return Result.Failure<(string, string?)>(
                new Error("PaymentMethodOverride.ActorRequired", "Actor user id is required.")
            );

        var normalizedMethod = method.Trim();
        if (normalizedMethod.Length > MethodMaxLength)
            return Result.Failure<(string, string?)>(
                new Error("PaymentMethodOverride.MethodTooLong", "Payment method is too long.")
            );

        if (enabled)
            return Result.Success<(string, string?)>((normalizedMethod, null));

        var reason = string.IsNullOrWhiteSpace(disabledReason) ? "DisabledByPlatform" : disabledReason.Trim();
        if (reason.Length > DisabledReasonMaxLength)
            reason = reason[..DisabledReasonMaxLength];

        return Result.Success<(string, string?)>((normalizedMethod, reason));
    }
}
