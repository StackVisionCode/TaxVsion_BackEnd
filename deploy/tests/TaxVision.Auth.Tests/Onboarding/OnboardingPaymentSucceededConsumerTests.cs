using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Onboarding.Application;
using TaxVision.Auth.Onboarding.Application.Consumers;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Commands;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 9 — UoW #3: cierra el ciclo pago→token. Emite el RegistrationToken (vía el
/// camino de éxito compartido), publica el evento de registro y encola el FINALIZE (Billing invoice +
/// commit de códigos). Ya NO pide recibo a Documents (esa responsabilidad pasó a Billing).</summary>
public sealed class OnboardingPaymentSucceededConsumerTests
{
    private static readonly OnboardingOptions RegistrationOptions = new()
    {
        RegistrationUrlBase = "https://app.example.com",
    };

    private static OnboardingSuccessCompleter BuildCompleter(FakeTokenReferenceStore tokenReferences) =>
        new(
            new SecureTokenService(),
            tokenReferences,
            Options.Create(RegistrationOptions),
            TaxVision.Auth.Tests.Application.FixedPlatformSettings.For("taxoffix.io"),
            NullLogger<OnboardingSuccessCompleter>.Instance
        );

    [Fact]
    public async Task Completes_payment_defers_registration_email_and_enqueues_finalize()
    {
        var now = DateTime.UtcNow;
        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        var paymentId = Guid.NewGuid();
        Assert.True(onboarding.MarkPaymentProcessing(paymentId, paymentId.ToString("N")).IsSuccess);

        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var tokenReferences = new FakeTokenReferenceStore();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();

        var evt = new OnboardingPaymentSucceededIntegrationEvent
        {
            OnboardingId = onboarding.Id,
            SaaSPaymentId = paymentId,
            PlanId = onboarding.PlanId,
            AmountPaidCents = 4900,
            Currency = "USD",
            PaidAtUtc = now,
            ProviderPaymentReference = "pi_123",
            PaymentMethodMasked = "Visa •••• 4242",
        };

        await OnboardingPaymentSucceededConsumer.Handle(
            evt,
            onboardings,
            BuildCompleter(tokenReferences),
            new FakePlanCatalogClient("Enterprise"),
            unitOfWork,
            bus,
            correlation,
            NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(TenantOnboardingStatus.RegistrationPending, onboarding.Status);
        Assert.NotNull(tokenReferences.Stored);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        // OBF-2.3: el email "completa tu oficina" sale INMEDIATO al liquidar el pago (antes se difería
        // 45 min al scheduler y cualquier falla intermedia dejaba huérfano al comprador). El evento se
        // publica una sola vez por aggregate (gate RegistrationEmailSentAtUtc) para no duplicar envíos.
        Assert.Contains(bus.Published, p => p is OnboardingRegistrationReadyIntegrationEvent);
        Assert.NotNull(onboarding.RegistrationEmailSentAtUtc);

        var finalize = Assert.IsType<OnboardingFinalizeCommand>(
            bus.Published.Single(p => p is OnboardingFinalizeCommand)
        );
        Assert.Equal(onboarding.Id, finalize.OnboardingId);
        Assert.Equal(paymentId, finalize.PaymentId);
        // Sin códigos: el neto cobrado = bruto = lo pagado, sin descuento (SettlementType Paid).
        Assert.Equal(4900, finalize.GrossAmountCents);
        Assert.Equal(0, finalize.DiscountAmountCents);
        Assert.Equal(4900, finalize.NetAmountCents);

        // Re-cableado: además del FINALIZE, se pide el recibo de pago a Documents (dato crudo; el
        // handler enmascara la referencia del proveedor).
        var receipt = Assert.IsType<RequestOnboardingReceiptCommand>(
            bus.Published.Single(p => p is RequestOnboardingReceiptCommand)
        );
        Assert.Equal(onboarding.Id, receipt.OnboardingId);
        Assert.Equal("Ada", receipt.PayerFirstName);
        Assert.Equal("Lovelace", receipt.PayerLastName);
        Assert.Equal("buyer@example.com", receipt.PayerEmail);
        Assert.Equal("Enterprise", receipt.PlanName);
        Assert.Equal(4900, receipt.PricePaidCents);
        Assert.Equal("USD", receipt.Currency);
        Assert.Equal("pi_123", receipt.ProviderPaymentReference);
        Assert.Equal("Visa •••• 4242", receipt.PaymentMethodMasked);
    }

    [Fact]
    public async Task Completes_an_onboarding_the_sweeper_expired_while_its_payment_was_in_flight()
    {
        var now = DateTime.UtcNow;
        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        var paymentId = Guid.NewGuid();
        Assert.True(onboarding.MarkPaymentProcessing(paymentId, paymentId.ToString("N")).IsSuccess);
        Assert.True(onboarding.MarkExpired().IsSuccess);

        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var tokenReferences = new FakeTokenReferenceStore();
        var bus = new FakeMessageBus();

        await OnboardingPaymentSucceededConsumer.Handle(
            new OnboardingPaymentSucceededIntegrationEvent
            {
                OnboardingId = onboarding.Id,
                SaaSPaymentId = paymentId,
                PlanId = onboarding.PlanId,
                AmountPaidCents = 4900,
                Currency = "USD",
                PaidAtUtc = now,
                ProviderPaymentReference = "pi_late",
            },
            onboardings,
            BuildCompleter(tokenReferences),
            new FakePlanCatalogClient("Enterprise"),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
            CancellationToken.None
        );

        // El cobro llegó tarde pero es de este checkout: la oficina sigue su registro.
        Assert.Equal(TenantOnboardingStatus.RegistrationPending, onboarding.Status);
        Assert.Contains(bus.Published, p => p is OnboardingFinalizeCommand);
    }

    [Fact]
    public async Task Payment_notice_arriving_after_the_reconcile_completed_the_onboarding_ends_quietly()
    {
        var now = DateTime.UtcNow;
        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        var paymentId = Guid.NewGuid();
        Assert.True(onboarding.MarkPaymentProcessing(paymentId, paymentId.ToString("N")).IsSuccess);
        // Lo que dejó en la base la conciliación del landing, que completó el alta mientras este aviso corría.
        var completedByReconcile = OnboardingTestFactory.NewOnboarding(now);
        Assert.True(completedByReconcile.MarkPaymentProcessing(paymentId, paymentId.ToString("N")).IsSuccess);
        Assert.True(completedByReconcile.MarkPaymentCompleted(paymentId.ToString("N"), now).IsSuccess);
        var bus = new FakeMessageBus();

        await HandleAsync(
            new FakeTenantOnboardingRepository { Existing = onboarding, Reloaded = completedByReconcile },
            new ConflictOnFirstSaveUnitOfWork(),
            bus,
            Succeeded(onboarding, paymentId, now)
        );

        // Sin error ni reencolado: el cobro ya está asentado y no se repiten correo ni cierre.
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Payment_notice_still_fails_when_the_conflict_left_the_payment_unrecorded()
    {
        var now = DateTime.UtcNow;
        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        var paymentId = Guid.NewGuid();
        Assert.True(onboarding.MarkPaymentProcessing(paymentId, paymentId.ToString("N")).IsSuccess);
        // Otro proceso (el job de vencimiento) cambió la fila sin asentar el cobro.
        var expiredBySweeper = OnboardingTestFactory.NewOnboarding(now);
        Assert.True(expiredBySweeper.MarkPaymentProcessing(paymentId, paymentId.ToString("N")).IsSuccess);
        Assert.True(expiredBySweeper.MarkExpired().IsSuccess);

        // Se relanza para que Wolverine reintente y el cobro no se pierda.
        await Assert.ThrowsAsync<ConflictException>(() =>
            HandleAsync(
                new FakeTenantOnboardingRepository { Existing = onboarding, Reloaded = expiredBySweeper },
                new ConflictOnFirstSaveUnitOfWork(),
                new FakeMessageBus(),
                Succeeded(onboarding, paymentId, now)
            )
        );
    }

    private static OnboardingPaymentSucceededIntegrationEvent Succeeded(
        TenantOnboarding onboarding,
        Guid paymentId,
        DateTime now
    ) =>
        new()
        {
            OnboardingId = onboarding.Id,
            SaaSPaymentId = paymentId,
            PlanId = onboarding.PlanId,
            AmountPaidCents = 4900,
            Currency = "USD",
            PaidAtUtc = now,
            ProviderPaymentReference = "pi_race",
        };

    private static Task HandleAsync(
        FakeTenantOnboardingRepository onboardings,
        BuildingBlocks.Persistence.IUnitOfWork unitOfWork,
        FakeMessageBus bus,
        OnboardingPaymentSucceededIntegrationEvent evt
    ) =>
        OnboardingPaymentSucceededConsumer.Handle(
            evt,
            onboardings,
            BuildCompleter(new FakeTokenReferenceStore()),
            new FakePlanCatalogClient("Enterprise"),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Ignores_events_for_unknown_onboardings()
    {
        var onboardings = new FakeTenantOnboardingRepository();
        var tokenReferences = new FakeTokenReferenceStore();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();

        var evt = new OnboardingPaymentSucceededIntegrationEvent
        {
            OnboardingId = Guid.NewGuid(),
            SaaSPaymentId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            AmountPaidCents = 4900,
            Currency = "USD",
            PaidAtUtc = DateTime.UtcNow,
            ProviderPaymentReference = "pi_123",
        };

        await OnboardingPaymentSucceededConsumer.Handle(
            evt,
            onboardings,
            BuildCompleter(tokenReferences),
            new FakePlanCatalogClient("Enterprise"),
            unitOfWork,
            bus,
            correlation,
            NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
        Assert.Null(tokenReferences.Stored);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Completer_does_not_store_raw_registration_token_when_state_transition_fails()
    {
        var onboarding = OnboardingTestFactory.NewOnboarding(DateTime.UtcNow);
        var tokenReferences = new FakeTokenReferenceStore();
        var bus = new FakeMessageBus();

        var result = await BuildCompleter(tokenReferences)
            .CompleteAsync(
                onboarding,
                amountPaidCents: 4900,
                currency: "USD",
                paymentId: Guid.NewGuid(),
                planName: "Enterprise",
                paidAtUtc: DateTime.UtcNow,
                providerPaymentReference: "pi_123",
                paymentMethodMasked: "Visa **** 4242",
                correlationId: "corr-test",
                sendRegistrationEmailNow: false,
                ct: CancellationToken.None
            );

        Assert.True(result.IsFailure);
        Assert.Null(tokenReferences.Stored);
        Assert.Null(tokenReferences.StoredReference);
        Assert.Empty(bus.Published);
    }
}
