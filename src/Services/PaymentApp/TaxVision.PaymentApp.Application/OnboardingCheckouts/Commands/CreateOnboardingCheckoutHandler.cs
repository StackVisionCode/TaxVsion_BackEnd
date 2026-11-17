using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common.Branding;
using TaxVision.PaymentApp.Application.Common.HostedCheckout;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.OnboardingCheckouts.Commands;

/// <summary>
/// Crea (o replaya) la sesión de checkout hosteada del primer pago de un onboarding pago-primero. Los pasos
/// comunes los pone <see cref="HostedCheckoutPipeline"/>; acá vive lo propio del onboarding: el catálogo de
/// métodos habilitados, el precio resuelto contra Subscription (no viene del caller) y una idempotencia
/// distinta a la de los demás checkouts — este reintenta en sitio sobre el mismo pago.
/// </summary>
public static class CreateOnboardingCheckoutHandler
{
    public static async Task<Result<OnboardingCheckoutResponse>> Handle(
        CreateOnboardingCheckoutCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        IOnboardingPaymentMethodCatalog paymentMethodCatalog,
        ISubscriptionPlanPricingClient planPricing,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        IPlatformSettings platform,
        ICheckoutReturnUrlGuard returnUrls,
        CancellationToken ct
    )
    {
        var request = new HostedCheckoutRequest(
            command.IdempotencyKey,
            command.PayerEmail,
            command.SuccessUrl,
            command.CancelUrl,
            command.Provider,
            command.Method
        );

        var result = await HostedCheckoutPipeline.RunAsync(
            request,
            PolicyFor(command, paymentMethodCatalog, planPricing, platform.Current),
            payments,
            providerFactory,
            audit,
            unitOfWork,
            metrics,
            correlation,
            logger,
            returnUrls,
            ct
        );

        return result.IsFailure
            ? Result.Failure<OnboardingCheckoutResponse>(result.Error)
            : Result.Success(
                new OnboardingCheckoutResponse(
                    result.Value.PaymentId,
                    result.Value.CheckoutUrl,
                    result.Value.ProviderSessionId,
                    result.Value.ExpiresAtUtc
                )
            );
    }

    private static HostedCheckoutPolicy PolicyFor(
        CreateOnboardingCheckoutCommand command,
        IOnboardingPaymentMethodCatalog paymentMethodCatalog,
        ISubscriptionPlanPricingClient planPricing,
        PlatformSettingsSnapshot platform
    )
    {
        // El bruto lo resuelve Subscription al calcular el monto; las promociones deben cuadrar con él.
        long grossCents = 0;
        return new()
        {
            PaymentType = SaaSPaymentType.OnboardingInitial,
            Subject = "Onboarding checkout",
            ReferenceId = command.OnboardingId,
            StatementDescriptor = PlatformStatementDescriptors.For(platform, "SAAS"),
            PreCheck = ct => EnsureCheckoutMethodEnabledAsync(command, paymentMethodCatalog, ct),
            ResolveAmount = async ct =>
            {
                var resolved = await OnboardingCheckoutAmount.ResolveAsync(command, planPricing, ct);
                if (resolved.IsFailure)
                    return Result.Failure<Money>(resolved.Error);
                grossCents = resolved.Value.GrossCents;
                return Result.Success(resolved.Value.Charge);
            },
            CreatePayment = (key, amount, descriptor, nowUtc) =>
                OnboardingCheckoutPromotions.Attach(
                    SaaSPayment
                        .CreateForOnboarding(
                            command.OnboardingId,
                            key,
                            amount,
                            command.PlanId,
                            command.Provider,
                            descriptor,
                            nowUtc,
                            command.ReferralDeclared
                        )
                        .WithConceptFor(SaaSPaymentType.OnboardingInitial, displayName: command.PlanDisplayName),
                    command,
                    grossCents
                ),
            DecideOnExisting = (existing, nowUtc) => DecideOnExisting(command, existing, nowUtc),
            ProviderKey = ProviderKeyFor,
            BuildMetadata = payment => new Dictionary<string, string>
            {
                ["onboardingId"] = command.OnboardingId.ToString("N"),
                ["saaSPaymentId"] = payment.Id.ToString("N"),
            },
            BuildAuditPayload = (payment, session) =>
                new
                {
                    payment.Status,
                    OnboardingId = command.OnboardingId,
                    session.ProviderSessionId,
                },
        };
    }

    private static async Task<Result> EnsureCheckoutMethodEnabledAsync(
        CreateOnboardingCheckoutCommand command,
        IOnboardingPaymentMethodCatalog paymentMethodCatalog,
        CancellationToken ct
    )
    {
        var availability = await paymentMethodCatalog.EnsureEnabledAsync(
            command.Provider,
            command.Method,
            command.PlanId,
            command.BillingCycle,
            command.Currency,
            ct
        );

        return availability.IsSuccess ? Result.Success() : Result.Failure(availability.Error);
    }

    /// <summary>Replay del intento vigente, reintento en sitio de uno fallido, o rechazo. Un webhook viejo no
    /// puede colarse: al reintentar se limpia la referencia externa del intento anterior.</summary>
    private static Result<ExistingPaymentDecision> DecideOnExisting(
        CreateOnboardingCheckoutCommand command,
        SaaSPayment existing,
        DateTime nowUtc
    )
    {
        if (
            HostedCheckoutPipeline.TryBuildResponse(existing) is { } replay
            && existing.Status is PaymentStatus.Pending or PaymentStatus.Processing or PaymentStatus.RequiresAction
        )
            return Result.Success(ExistingPaymentDecision.Replay(replay));

        if (existing.Status is PaymentStatus.Failed or PaymentStatus.Cancelled)
        {
            // Reintento con reservas renovadas: el pago apunta a las vigentes (mismo descuento).
            var renewed = OnboardingCheckoutPromotions.Renew(existing, command);
            if (renewed.IsFailure)
                return Result.Failure<ExistingPaymentDecision>(renewed.Error);

            var prep = existing.PrepareForOnboardingRetry(nowUtc);
            return prep.IsFailure
                ? Result.Failure<ExistingPaymentDecision>(prep.Error)
                : Result.Success(ExistingPaymentDecision.Retry(existing));
        }

        return Result.Failure<ExistingPaymentDecision>(
            new Error("Onboarding.Checkout.NotRetryable", $"This checkout cannot be re-created from {existing.Status}.")
        );
    }

    /// <summary>Clave del proveedor POR INTENTO: el primero usa la base; cada reintento le añade el número de
    /// intento. Sin esto, reintentar reusaría la clave y el proveedor devolvería la sesión vieja (idempotente)
    /// en vez de crear una nueva y cobrable.</summary>
    private static Result<IdempotencyKey> ProviderKeyFor(SaaSPayment payment) =>
        payment.Attempts.Count == 0
            ? Result.Success(payment.IdempotencyKey)
            : IdempotencyKey.Create($"{payment.IdempotencyKey.Value}-{payment.Attempts.Count}");
}
