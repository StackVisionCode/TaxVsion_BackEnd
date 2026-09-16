using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;

/// <summary>
/// PayFlow (Fase 16) + Gift/Referral — inicia el checkout. Los códigos (referido/promo/gift) son
/// OPCIONALES; si vienen, se cotizan+reservan apilados en Growth ANTES de crear el pago. Bifurca:
/// net &gt; 0 → PaymentApp cobra el NETO; net = 0 → SIN pago (cubierto 100%), se dispara el camino de éxito.
/// </summary>
public sealed record StartOnboardingCheckoutCommand(
    Guid OnboardingId,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string Provider = "Stripe",
    string Method = "Card",
    string? ReferralCode = null,
    string? PromoCode = null,
    string? GiftCode = null
);

/// <summary><see cref="FullyCovered"/> = un código cubrió el 100%: no hay pago ni CheckoutUrl; el
/// comprador recibe directamente el email con el link de registro.</summary>
public sealed record StartOnboardingCheckoutResponse(
    Guid PaymentId,
    string CheckoutUrl,
    DateTime ExpiresAtUtc,
    bool FullyCovered = false,
    // Desglose para que la UI muestre bruto→descuento→neto. Null cuando no se aplicó ningún código
    // (el frontend cae al precio del plan del catálogo).
    long? GrossAmountCents = null,
    long? DiscountAmountCents = null,
    long? NetAmountCents = null,
    string? Currency = null
);

public static class StartOnboardingCheckoutHandler
{
    public static async Task<Result<StartOnboardingCheckoutResponse>> Handle(
        StartOnboardingCheckoutCommand command,
        ITenantOnboardingRepository onboardings,
        OnboardingCodeReserver reserver,
        OnboardingSuccessCompleter successCompleter,
        IPlanCatalogClient planCatalog,
        IPaymentAppOnboardingClient paymentApp,
        IOnboardingReturnReferenceStore returnReferences,
        IOptions<OnboardingOptions> onboardingOptions,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure<StartOnboardingCheckoutResponse>(
                new Error("Onboarding.NotFound", "Onboarding not found.")
            );

        // El pagador es el email del onboarding. En sesión (cookie) el request lo trae y DEBE coincidir;
        // en el resume por token del email (que ya autorizó) el request lo deja vacío y se usa el del
        // onboarding directamente, sin exigir la cookie (el link puede abrirse en otro navegador).
        var payerEmail = string.IsNullOrWhiteSpace(command.PayerEmail) ? onboarding.Email : command.PayerEmail.Trim();
        if (!string.Equals(onboarding.Email, payerEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<StartOnboardingCheckoutResponse>(
                new Error("Onboarding.PayerEmailMismatch", "The payer email does not match this onboarding.")
            );
        }

        // Reintento: si el pago anterior falló, reabrimos (tope + ventana) antes de recobrar. El checkout
        // de abajo reusa el mismo SaaSPayment del lado de PaymentApp con un provider key nuevo por intento,
        // así que no hay doble cobro.
        if (onboarding.Status == TenantOnboardingStatus.PaymentFailed)
        {
            var options = onboardingOptions.Value;
            var reopen = onboarding.ReopenForPaymentRetry(
                DateTime.UtcNow,
                options.PaymentRetryMaxAttempts,
                TimeSpan.FromHours(options.PaymentRetryWindowHours)
            );
            if (reopen.IsFailure)
                return Result.Failure<StartOnboardingCheckoutResponse>(reopen.Error);
        }

        var codes = BuildCodeInputs(command);
        if (codes.Count > 0 && onboarding.CodeReservations.Count == 0)
        {
            var reserveResult = await reserver.ReserveAsync(onboarding, codes, ct);
            if (reserveResult.IsFailure)
                return Result.Failure<StartOnboardingCheckoutResponse>(reserveResult.Error);

            var outcome = reserveResult.Value;
            var applied = onboarding.ApplyOnboardingPricing(
                outcome.GrossAmountCents,
                outcome.TotalDiscountCents,
                outcome.NetAmountCents,
                outcome.Currency,
                referralAttributionId: null, // recompensa al referidor = follow-up (atribución pre-tenant)
                outcome.Reservations,
                DateTime.UtcNow
            );
            if (applied.IsFailure)
                return Result.Failure<StartOnboardingCheckoutResponse>(applied.Error);
        }

        // 2) Carril $0: cubierto 100% por código → SIN PaymentApp/Stripe.
        if (onboarding.FullyCovered)
            return await CompleteFullyCoveredAsync(
                onboarding,
                successCompleter,
                planCatalog,
                unitOfWork,
                correlation,
                ct
            );

        // 3) Carril con cobro: PaymentApp cobra el NETO (o el bruto si no hubo códigos).
        var idempotencyKey = $"onboarding-checkout-{onboarding.Id:N}";
        var primaryReservation = onboarding.CodeReservations.OrderBy(r => r.Order).FirstOrDefault();

        // Referencia de retorno opaca en el successUrl: permite reconciliar al volver de Stripe aunque
        // falte la cookie (otro navegador/incógnito). No es credencial de registro; TTL corto.
        var returnReference = await returnReferences.IssueAsync(
            onboarding.Id,
            TimeSpan.FromMinutes(onboardingOptions.Value.OnboardingReturnReferenceTtlMinutes),
            ct
        );
        var successUrl = AppendReturnReference(command.SuccessUrl, returnReference);

        var checkoutResult = await paymentApp.CreateCheckoutAsync(
            new PaymentAppCheckoutRequest(
                onboarding.Id,
                onboarding.PlanId,
                payerEmail,
                successUrl,
                command.CancelUrl,
                idempotencyKey,
                Provider: command.Provider,
                Method: command.Method,
                BillingCycle: onboarding.BillingCycle,
                NetAmountCents: onboarding.NetAmountCents,
                DiscountAmountCents: onboarding.TotalDiscountCents,
                Currency: onboarding.Currency,
                CodeReservationId: primaryReservation?.CodeReservationId,
                PromotionSnapshotHash: primaryReservation?.SnapshotHash
            ),
            ct
        );
        if (checkoutResult.IsFailure)
            return Result.Failure<StartOnboardingCheckoutResponse>(checkoutResult.Error);

        var markResult = onboarding.MarkPaymentProcessing(
            checkoutResult.Value.PaymentId,
            checkoutResult.Value.PaymentId.ToString("N")
        );
        if (markResult.IsFailure)
            return Result.Failure<StartOnboardingCheckoutResponse>(markResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new StartOnboardingCheckoutResponse(
                checkoutResult.Value.PaymentId,
                checkoutResult.Value.CheckoutUrl,
                checkoutResult.Value.ExpiresAtUtc,
                FullyCovered: false,
                GrossAmountCents: onboarding.GrossAmountCents,
                DiscountAmountCents: onboarding.TotalDiscountCents,
                NetAmountCents: onboarding.NetAmountCents,
                Currency: onboarding.Currency
            )
        );
    }

    private static async Task<Result<StartOnboardingCheckoutResponse>> CompleteFullyCoveredAsync(
        TenantOnboarding onboarding,
        OnboardingSuccessCompleter successCompleter,
        IPlanCatalogClient planCatalog,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var markCovered = onboarding.MarkFullyCoveredByCode(nowUtc);
        if (markCovered.IsFailure)
            return Result.Failure<StartOnboardingCheckoutResponse>(markCovered.Error);

        var planName = await planCatalog.GetPlanNameAsync(onboarding.PlanId, ct);
        var correlationId = string.IsNullOrWhiteSpace(correlation.CorrelationId)
            ? onboarding.Id.ToString("N")
            : correlation.CorrelationId;

        var completed = await successCompleter.CompleteAsync(
            onboarding,
            amountPaidCents: 0,
            currency: onboarding.Currency ?? "USD",
            paymentId: null,
            planName,
            nowUtc,
            providerPaymentReference: null,
            paymentMethodMasked: null,
            correlationId,
            sendRegistrationEmailNow: true,
            ct: ct
        );
        if (completed.IsFailure)
            return Result.Failure<StartOnboardingCheckoutResponse>(completed.Error);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new StartOnboardingCheckoutResponse(
                Guid.Empty,
                string.Empty,
                nowUtc,
                FullyCovered: true,
                GrossAmountCents: onboarding.GrossAmountCents,
                DiscountAmountCents: onboarding.TotalDiscountCents,
                NetAmountCents: onboarding.NetAmountCents,
                Currency: onboarding.Currency
            )
        );
    }

    private static string AppendReturnReference(string successUrl, string reference)
    {
        var separator = successUrl.Contains('?') ? '&' : '?';
        return $"{successUrl}{separator}r={Uri.EscapeDataString(reference)}";
    }

    private static List<OnboardingCodeInput> BuildCodeInputs(StartOnboardingCheckoutCommand command)
    {
        var codes = new List<OnboardingCodeInput>();
        if (!string.IsNullOrWhiteSpace(command.ReferralCode))
            codes.Add(
                new OnboardingCodeInput(
                    command.ReferralCode.Trim(),
                    OnboardingBenefitType.Referral,
                    command.ReferralCode.Trim()
                )
            );
        if (!string.IsNullOrWhiteSpace(command.PromoCode))
            codes.Add(
                new OnboardingCodeInput(command.PromoCode.Trim(), OnboardingBenefitType.Promo, command.PromoCode.Trim())
            );
        if (!string.IsNullOrWhiteSpace(command.GiftCode))
            codes.Add(
                new OnboardingCodeInput(command.GiftCode.Trim(), OnboardingBenefitType.Gift, command.GiftCode.Trim())
            );
        return codes;
    }
}
