using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Onboarding.IntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// El pago de un onboarding nace con <c>TenantId = Guid.Empty</c> porque el tenant todavía no existe, así que
/// su dueño no puede verlo. Cuando la saga crea el tenant, pasa a ser suyo.
/// </summary>
public sealed class OnboardingPaymentRehomeConsumerTests
{
    [Fact]
    public async Task The_onboarding_payment_moves_to_the_tenant_that_was_just_created()
    {
        var onboardingId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var payment = OnboardingPayment(onboardingId);
        var payments = new FakeSaaSPaymentRepository { Onboarding = payment };
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(onboardingId, tenantId, payments, unitOfWork);

        Assert.Equal(tenantId, payment.TenantId);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task A_redelivery_changes_nothing()
    {
        var onboardingId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var payment = OnboardingPayment(onboardingId);
        var payments = new FakeSaaSPaymentRepository { Onboarding = payment };
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(onboardingId, tenantId, payments, unitOfWork);
        await HandleAsync(onboardingId, tenantId, payments, unitOfWork);

        Assert.Equal(tenantId, payment.TenantId);
        // La segunda pasada no vuelve a escribir: el agregado ya era de ese tenant.
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    // El carril gratuito no crea pago: el consumer no puede romperse por eso.
    [Fact]
    public async Task An_onboarding_without_a_payment_is_ignored()
    {
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Guid.NewGuid(), Guid.NewGuid(), new FakeSaaSPaymentRepository(), unitOfWork);

        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public void A_payment_that_is_not_from_an_onboarding_cannot_be_re_homed()
    {
        var payment = SaaSPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create("seat-checkout-x").Value,
                Money.Create(1500, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SEATS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        var result = payment.RehomeToTenant(Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.NotOnboarding", result.Error.Code);
    }

    private static Task HandleAsync(
        Guid onboardingId,
        Guid tenantId,
        FakeSaaSPaymentRepository payments,
        FakeUnitOfWork unitOfWork
    ) =>
        OnboardingPaymentRehomeConsumer.Handle(
            new TenantCreatedForOnboardingIntegrationEvent { OnboardingId = onboardingId, CreatedTenantId = tenantId },
            payments,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

    private static SaaSPayment OnboardingPayment(Guid onboardingId) =>
        SaaSPayment
            .CreateForOnboarding(
                onboardingId,
                IdempotencyKey.Create($"onb-{onboardingId:N}").Value,
                Money.Create(4900, "USD").Value,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                DateTime.UtcNow
            )
            .Value;
}
