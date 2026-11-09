using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.Billing;

/// <summary>El pedido de cambio de modo, tal como llega.</summary>
public sealed record BillingPreferenceRequest(
    string? ProviderCode,
    Guid ActorUserId,
    string? ConsentText,
    string? ConsentIpAddress,
    string? ConsentUserAgent
);

/// <summary>Lo acordado para el mandato. Sale siempre del plan vigente, nunca del pedido.</summary>
public sealed record MandateTerms(long AmountCents, string Currency, string Cadence)
{
    public static readonly MandateTerms None = new(0, "USD", "monthly");
}

/// <summary>Reglas para elegir o cambiar el modo de cobro de un tenant.</summary>
public static class BillingPreferencePolicy
{
    public static Result<BillingMode> ParseRequest(string? mode, BillingPreferenceRequest request)
    {
        if (!Enum.TryParse<BillingMode>(mode, ignoreCase: true, out var parsed))
            return Result.Failure<BillingMode>(
                new Error("BillingPreference.InvalidMode", $"'{mode}' is not a valid mode.")
            );

        if (parsed != BillingMode.Automatic)
            return Result.Success(parsed);

        if (string.IsNullOrWhiteSpace(request.ProviderCode))
            return Result.Failure<BillingMode>(
                new Error("BillingPreference.InvalidProvider", "ProviderCode is required for Automatic mode.")
            );
        if (string.IsNullOrWhiteSpace(request.ConsentText))
            return Result.Failure<BillingMode>(
                new Error("BillingPreference.MissingConsentText", "ConsentText is required for Automatic mode.")
            );
        if (string.IsNullOrWhiteSpace(request.ConsentIpAddress))
            return Result.Failure<BillingMode>(
                new Error("BillingPreference.MissingConsentIp", "ConsentIpAddress is required for Automatic mode.")
            );

        return Result.Success(parsed);
    }

    /// <summary>Crea la preferencia si no había, o cambia la que está. Devuelve la vigente.</summary>
    public static Result<TenantBillingPreference> Apply(
        TenantBillingPreference? existing,
        Guid tenantId,
        BillingMode mode,
        BillingPreferenceRequest request,
        MandateTerms terms,
        DateTime nowUtc
    )
    {
        if (existing is null)
            return TenantBillingPreference.Choose(
                tenantId,
                mode,
                request.ProviderCode,
                request.ActorUserId,
                request.ConsentText,
                request.ConsentIpAddress,
                request.ConsentUserAgent,
                terms.AmountCents,
                terms.Currency,
                terms.Cadence,
                nowUtc
            );

        var transition =
            mode == BillingMode.Automatic
                ? existing.SwitchToAutomatic(
                    request.ProviderCode!,
                    request.ActorUserId,
                    request.ConsentText!,
                    request.ConsentIpAddress!,
                    request.ConsentUserAgent,
                    terms.AmountCents,
                    terms.Currency,
                    terms.Cadence,
                    nowUtc
                )
                : existing.SwitchToManual(request.ActorUserId, nowUtc);

        return transition.IsFailure
            ? Result.Failure<TenantBillingPreference>(transition.Error)
            : Result.Success(existing);
    }
}
