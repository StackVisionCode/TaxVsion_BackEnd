using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;

/// <summary>
/// PayFlow (Fase 16) — deliberadamente sin precio/moneda en el request: hasta esta fase, el
/// caller (en última instancia el frontend anónimo) los enviaba sin validación server-side contra
/// el catálogo real de planes (gap documentado y aceptado en Fase 9, cerrado acá). PaymentApp
/// resuelve el precio real vía M2M a Subscription antes de crear el Stripe Checkout Session — ver
/// <c>CreateOnboardingCheckoutHandler</c>.
/// </summary>
public sealed record StartOnboardingCheckoutCommand(
    Guid OnboardingId,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl
);

public sealed record StartOnboardingCheckoutResponse(Guid PaymentId, string CheckoutUrl, DateTime ExpiresAtUtc);

/// <summary>
/// PayFlow (Fase 9) — llama al checkout M2M de PaymentApp (Fase 8) y avanza el onboarding a
/// <c>PaymentProcessing</c>. La <see cref="TenantOnboarding.MarkPaymentCompleted"/> posterior
/// (en <c>OnboardingPaymentSucceededConsumer</c>) exige que el <c>paymentReference</c> coincida
/// exactamente con el que se guardó acá — por eso se usa el <c>PaymentId</c> (Guid del
/// <c>SaaSPayment</c> en PaymentApp) como referencia estable en ambos lados, en vez de cualquier
/// identificador específico de Stripe (Checkout Session id / PaymentIntent id): ambos servicios
/// ya conocen ese mismo Guid de forma determinista (PaymentApp lo devuelve acá; PaymentApp
/// también lo publica como <c>SaaSPaymentId</c> en el evento de éxito), sin inventar plumbing
/// nuevo para transportar una referencia de Stripe que el checkout inicial ni siquiera conoce
/// todavía.
/// </summary>
public static class StartOnboardingCheckoutHandler
{
    public static async Task<Result<StartOnboardingCheckoutResponse>> Handle(
        StartOnboardingCheckoutCommand command,
        ITenantOnboardingRepository onboardings,
        IPaymentAppOnboardingClient paymentApp,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure<StartOnboardingCheckoutResponse>(
                new Error("Onboarding.NotFound", "Onboarding not found.")
            );

        var idempotencyKey = $"onboarding-checkout-{onboarding.Id:N}";

        var checkoutResult = await paymentApp.CreateCheckoutAsync(
            new PaymentAppCheckoutRequest(
                onboarding.Id,
                onboarding.PlanId,
                command.PayerEmail,
                command.SuccessUrl,
                command.CancelUrl,
                idempotencyKey
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
                checkoutResult.Value.ExpiresAtUtc
            )
        );
    }
}
