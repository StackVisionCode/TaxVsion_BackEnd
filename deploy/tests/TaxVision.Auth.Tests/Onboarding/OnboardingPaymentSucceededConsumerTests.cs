using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.Consumers;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Infrastructure.Security;
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

    private static OnboardingSuccessCompleter BuildCompleter(
        FakeTokenReferenceStore tokenReferences,
        FakeMessageBus bus
    ) =>
        new(
            new SecureTokenService(),
            tokenReferences,
            Options.Create(RegistrationOptions),
            bus,
            NullLogger<OnboardingSuccessCompleter>.Instance
        );

    [Fact]
    public async Task Completes_payment_publishes_registration_ready_and_enqueues_finalize()
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
            BuildCompleter(tokenReferences, bus),
            new FakePlanCatalogClient("Enterprise"),
            unitOfWork,
            correlation,
            NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(TenantOnboardingStatus.RegistrationPending, onboarding.Status);
        Assert.NotNull(tokenReferences.Stored);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var ready = Assert.IsType<OnboardingRegistrationReadyIntegrationEvent>(
            bus.Published.Single(p => p is OnboardingRegistrationReadyIntegrationEvent)
        );
        Assert.Equal(onboarding.Id, ready.OnboardingId);
        Assert.Equal(tokenReferences.StoredReference, ready.TokenReference);
        Assert.Equal("49.00 USD", ready.PriceFormatted);

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
            BuildCompleter(tokenReferences, bus),
            new FakePlanCatalogClient("Enterprise"),
            unitOfWork,
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

        var result = await BuildCompleter(tokenReferences, bus)
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
                CancellationToken.None
            );

        Assert.True(result.IsFailure);
        Assert.Null(tokenReferences.Stored);
        Assert.Null(tokenReferences.StoredReference);
        Assert.Empty(bus.Published);
    }
}
