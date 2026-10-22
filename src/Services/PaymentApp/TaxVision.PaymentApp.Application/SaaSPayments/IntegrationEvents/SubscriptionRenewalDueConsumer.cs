using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Billing.Abstractions;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.Common.Branding;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.Billing;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>
/// Rama el intent de renovación según la preferencia proyectada del tenant: cuando
/// hay consent activo y método guardado, cobra off-session con <see cref="IOffSessionCharger"/>
/// y publica el succeeded/failed de siempre; en otro caso cae al flow legacy
/// (<see cref="ChargeSaaSPaymentCommand"/>) para que el fallo con requires_payment_method
/// mande al tenant al banner "Pay now". El cobro off-session solo arranca si existe mandate del
/// mismo provider que la preferencia — un mandate revocado o un provider sin registrar fuerzan
/// también el fallback manual.
/// </summary>
public static class SubscriptionRenewalDueConsumer
{
    public static async Task Handle(
        SubscriptionRenewalDueIntegrationEvent evt,
        IMessageBus bus,
        ITenantBillingPreferenceProjectionRepository preferences,
        ITenantProviderCustomerRepository customers,
        IOffSessionChargerFactory offSessionFactory,
        ISaaSPaymentRepository payments,
        IPlatformSettings platform,
        IPaymentAppMetrics metrics,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            // Idempotencia: Wolverine puede reentregar el evento. Si ya hay un SaaSPayment con la
            // misma IdempotencyKey, no se vuelve a cobrar — un retry ve el mismo pago.
            var existing = await payments.GetByIdempotencyKeyAsync(evt.IdempotencyKey, ct);
            if (existing is not null)
            {
                logger.LogInformation(
                    "SaaSPayment already exists for renewal {IdempotencyKey}; skipping (idempotent).",
                    evt.IdempotencyKey
                );
                return;
            }

            var preference = await preferences.GetAsync(evt.TenantId, ct);
            var decision = ResolveStrategy(preference, customers, offSessionFactory);
            var strategy = await decision(evt, customers, offSessionFactory, ct);

            if (
                strategy.UseOffSession
                && strategy.Charger is not null
                && strategy.ProviderCode is not null
                && strategy.PaymentMethodToken is not null
            )
            {
                await HandleOffSessionAsync(
                    evt,
                    strategy.Charger,
                    strategy.ProviderCode.Value,
                    strategy.PaymentMethodToken,
                    payments,
                    platform,
                    metrics,
                    bus,
                    unitOfWork,
                    customers,
                    correlationId,
                    logger,
                    ct
                );
                return;
            }

            if (strategy.FallbackReason is not null)
            {
                logger.LogInformation(
                    "Off-session renewal not possible for tenant {TenantId}: {Reason}. Falling back to legacy charge flow.",
                    evt.TenantId,
                    strategy.FallbackReason
                );
            }

            await DispatchLegacyChargeAsync(evt, DunningProviderResolver.Resolve(preference), bus, logger, ct);
        }
    }

    // Resolver pequeño: encapsula las razones y devuelve una ChargeStrategy. El contrato que
    // garantiza NO cobrar sin mandate vive acá.
    private static Func<
        SubscriptionRenewalDueIntegrationEvent,
        ITenantProviderCustomerRepository,
        IOffSessionChargerFactory,
        CancellationToken,
        Task<ChargeStrategy>
    > ResolveStrategy(
        TenantBillingPreferenceProjection? preference,
        ITenantProviderCustomerRepository customers,
        IOffSessionChargerFactory offSessionFactory
    ) =>
        async (evt, repo, factory, ct) =>
        {
            if (preference is null)
                return ChargeStrategy.Fallback("No billing preference projected");

            if (!preference.AllowsOffSessionCharge)
                return ChargeStrategy.Fallback(
                    preference.Mode == TenantBillingPreferenceProjection.ModeManual
                        ? "Preference is Manual"
                        : "No active mandate or provider"
                );

            if (!TryParseProvider(preference.ProviderCode!, out var provider))
                return ChargeStrategy.Fallback($"Unknown provider code '{preference.ProviderCode}'");

            var charger = factory.For(provider.ToString());
            if (charger is null)
                return ChargeStrategy.Fallback($"No IOffSessionCharger registered for {provider}");

            var providerCustomer = await repo.GetByTenantAndProviderAsync(evt.TenantId, provider, ct);
            if (providerCustomer is null)
                return ChargeStrategy.Fallback("No TenantProviderCustomer for this tenant/provider");

            var defaultMethod = providerCustomer.GetDefaultMethod();
            if (defaultMethod is null)
                return ChargeStrategy.Fallback("No default saved payment method");

            return ChargeStrategy.OffSession(charger, provider, defaultMethod.MethodReference);
        };

    private static async Task HandleOffSessionAsync(
        SubscriptionRenewalDueIntegrationEvent evt,
        IOffSessionCharger charger,
        PaymentProviderCode provider,
        string paymentMethodToken,
        ISaaSPaymentRepository payments,
        IPlatformSettings platform,
        IPaymentAppMetrics metrics,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        ITenantProviderCustomerRepository customers,
        string correlationId,
        ILogger logger,
        CancellationToken ct
    )
    {
        var paymentResult = BuildPayment(evt, provider, platform);
        if (paymentResult.IsFailure)
        {
            logger.LogError(
                "Could not create SaaSPayment for off-session renewal: {Error}",
                paymentResult.Error.Message
            );
            await DispatchLegacyChargeAsync(evt, provider, bus, logger, ct);
            return;
        }
        var payment = paymentResult.Value;
        await payments.AddAsync(payment, ct);
        metrics.RecordAttempted(provider.ToString(), payment.Type.ToString());

        var request = new OffSessionChargeRequest(
            TenantId: evt.TenantId,
            SaaSPaymentId: payment.Id,
            ProviderCode: provider.ToString(),
            ProviderToken: paymentMethodToken,
            AmountCents: evt.AmountCents,
            Currency: evt.Currency,
            IdempotencyKey: evt.IdempotencyKey,
            PayerEmail: SyntheticPayer.EmailFor(evt.TenantId)
        );

        var chargeResult = await charger.ChargeAsync(request, ct);
        if (chargeResult.IsFailure)
        {
            SaaSPaymentChargeOutcome.FailPayment(
                payment,
                chargeResult.Error,
                Guid.Empty,
                SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, DateTime.UtcNow),
                metrics
            );
        }
        else
        {
            ApplyOffSessionOutcome(payment, chargeResult.Value, provider, metrics, logger);
        }

        await SaaSPaymentResultPublisher.PublishAsync(payment, bus, correlationId, ct, customers: customers);
        // Email de confirmación del cobro recurrente. Viaja por el canal de status-changed (no mueve el
        // estado). El de fallo ya no sale de acá: lo manda Subscription en cada intento fallido
        // (RenewalChargeFailedIntegrationEvent), con el intento, la fecha del próximo y la tarjeta real.
        await PublishRecurringChargeSucceededEmailAsync(evt, chargeResult, correlationId, bus);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Off-session renewal for tenant {TenantId} finished with status {Status} (provider {Provider}, last4 shown by the charger log, never here).",
            evt.TenantId,
            payment.Status,
            provider
        );
    }

    private static void ApplyOffSessionOutcome(
        SaaSPayment payment,
        OffSessionChargeOutcome outcome,
        PaymentProviderCode provider,
        IPaymentAppMetrics metrics,
        ILogger logger
    )
    {
        var nowUtc = DateTime.UtcNow;

        // El provider devuelve una referencia útil solo cuando pudo crear el intent — por eso no se
        // exige para los Failed sin referencia (red caída, auth fallida antes de hablar con el banco).
        if (!string.IsNullOrWhiteSpace(outcome.ProviderPaymentReference))
        {
            var referenceResult = ExternalPaymentReference.Create(provider, outcome.ProviderPaymentReference);
            if (referenceResult.IsSuccess)
            {
                payment.MarkProcessing(
                    referenceResult.Value,
                    outcome.Status.ToString(),
                    providerResponseBody: null,
                    actorUserId: Guid.Empty,
                    nowUtc
                );
            }
        }

        switch (outcome.Status)
        {
            case OffSessionChargeStatus.Succeeded:
                if (payment.Status == PaymentStatus.Pending)
                {
                    // Sin referencia no se puede marcar Processing — pasar directo a Succeeded igual
                    // publicaría el succeeded (que incluye ExternalPaymentReference en el evento),
                    // así que trato esto como Failed sin registro: el flow manual retomará.
                    SaaSPaymentChargeOutcome.FailPayment(
                        payment,
                        new Error("OffSession.MissingReference", "Provider returned Succeeded with no reference."),
                        Guid.Empty,
                        SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, nowUtc),
                        metrics
                    );
                    return;
                }
                payment.MarkSucceeded(nowUtc, Guid.Empty);
                metrics.RecordSucceeded(provider.ToString(), payment.Type.ToString());
                break;
            case OffSessionChargeStatus.RequiresUserAction:
                // SCA/3DS: marcar RequiresAction y DEJAR que el publisher caiga al legacy — el
                // RequiresAction no es un fallo pero tampoco cierra el intent; el banner "Pay now"
                // de la landing arranca el on-session cuando el user vuelve.
                if (payment.Status == PaymentStatus.Pending)
                {
                    SaaSPaymentChargeOutcome.FailPayment(
                        payment,
                        new Error(
                            outcome.FailureCode ?? "authentication_required",
                            outcome.FailureMessage ?? "Issuing bank requires customer authentication."
                        ),
                        Guid.Empty,
                        SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, nowUtc),
                        metrics
                    );
                }
                else
                {
                    payment.MarkRequiresAction(
                        outcome.FailureCode ?? "authentication_required",
                        nextActionUrl: string.Empty,
                        actorUserId: Guid.Empty,
                        nowUtc
                    );
                }
                logger.LogInformation(
                    "Off-session renewal requires user action (3DS/SCA) for payment {PaymentId}; fallback to Pay now banner.",
                    payment.Id
                );
                break;
            case OffSessionChargeStatus.Failed:
            default:
                SaaSPaymentChargeOutcome.FailPayment(
                    payment,
                    new Error(
                        outcome.FailureCode ?? "Provider.Failed",
                        outcome.FailureMessage ?? "Off-session charge was declined."
                    ),
                    Guid.Empty,
                    // Con referencia, el intento ya quedó registrado en MarkProcessing: no contarlo dos veces.
                    SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(
                        payment,
                        nowUtc,
                        failedAttemptRecorded: payment.Status == PaymentStatus.Processing
                    ),
                    metrics
                );
                break;
        }
    }

    private static Result<SaaSPayment> BuildPayment(
        SubscriptionRenewalDueIntegrationEvent evt,
        PaymentProviderCode provider,
        IPlatformSettings platform
    )
    {
        var keyResult = IdempotencyKey.Create(evt.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<SaaSPayment>(keyResult.Error);

        var amountResult = Money.Create(evt.AmountCents, evt.Currency);
        if (amountResult.IsFailure)
            return Result.Failure<SaaSPayment>(amountResult.Error);

        var descriptorResult = StatementDescriptor.Create(PlatformStatementDescriptors.For(platform.Current, "SAAS"));
        if (descriptorResult.IsFailure)
            return Result.Failure<SaaSPayment>(descriptorResult.Error);

        return SaaSPayment
            .Create(
                evt.TenantId,
                keyResult.Value,
                amountResult.Value,
                SaaSPaymentType.SubscriptionRenewal,
                evt.TenantSubscriptionId,
                provider,
                descriptorResult.Value,
                actorUserId: Guid.Empty,
                DateTime.UtcNow,
                evt.CodeReservationId,
                evt.CodeReservationPaymentId,
                evt.DiscountAmountCents,
                evt.PromotionSnapshotHash
            )
            .WithConceptFor(SaaSPaymentType.SubscriptionRenewal);
    }

    private static async Task DispatchLegacyChargeAsync(
        SubscriptionRenewalDueIntegrationEvent evt,
        PaymentProviderCode provider,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        // Respetar el provider del tenant también en el fallback: antes se hardcodeaba Stripe y la
        // renovación entraba en loop de "Failed — provider declined" para cualquier tenant con PayPal.
        // El provider lo resuelven los callers vía DunningProviderResolver con la preference del tenant.
        var command = new ChargeSaaSPaymentCommand(
            TenantId: evt.TenantId,
            IdempotencyKey: evt.IdempotencyKey,
            AmountCents: evt.AmountCents,
            Currency: evt.Currency,
            Type: SaaSPaymentType.SubscriptionRenewal,
            TargetAggregateId: evt.TenantSubscriptionId,
            Provider: provider,
            PayerEmail: SyntheticPayer.EmailFor(evt.TenantId),
            PayerName: null,
            RequestedByUserId: Guid.Empty,
            CodeReservationId: evt.CodeReservationId,
            CodeReservationPaymentId: evt.CodeReservationPaymentId,
            DiscountAmountCents: evt.DiscountAmountCents,
            PromotionSnapshotHash: evt.PromotionSnapshotHash
        );

        var result = await bus.InvokeAsync<Result<Guid>>(command, ct);
        if (result.IsFailure)
        {
            logger.LogError(
                "ChargeSaaSPayment (legacy fallback) failed for renewal of subscription {TenantSubscriptionId} (provider {Provider}): {Code} — {Msg}",
                evt.TenantSubscriptionId,
                provider,
                result.Error.Code,
                result.Error.Message
            );
        }
    }

    private static bool TryParseProvider(string code, out PaymentProviderCode provider) =>
        Enum.TryParse(code, ignoreCase: true, out provider);

    private static async Task PublishRecurringChargeSucceededEmailAsync(
        SubscriptionRenewalDueIntegrationEvent evt,
        Result<OffSessionChargeOutcome> chargeResult,
        string correlationId,
        IMessageBus bus
    )
    {
        if (!chargeResult.IsSuccess || chargeResult.Value.Status != OffSessionChargeStatus.Succeeded)
            return;

        await bus.PublishAsync(
            new TenantSubscriptionStatusChangedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = correlationId,
                TenantSubscriptionId = evt.TenantSubscriptionId,
                Status = "Active",
                PreviousStatus = "Active",
                Reason = nameof(SubscriptionChangeReason.RecurringChargeSucceeded),
                Amount = (evt.AmountCents / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                DateCharged = DateTime.UtcNow.ToString(
                    "MMMM d, yyyy",
                    System.Globalization.CultureInfo.InvariantCulture
                ),
            }
        );
    }

    private readonly record struct ChargeStrategy(
        bool UseOffSession,
        IOffSessionCharger? Charger,
        PaymentProviderCode? ProviderCode,
        string? PaymentMethodToken,
        string? FallbackReason
    )
    {
        public static ChargeStrategy OffSession(IOffSessionCharger charger, PaymentProviderCode code, string token) =>
            new(UseOffSession: true, charger, code, token, FallbackReason: null);

        public static ChargeStrategy Fallback(string reason) =>
            new(UseOffSession: false, Charger: null, ProviderCode: null, PaymentMethodToken: null, reason);
    }
}
