using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.SaaSPayments.Events;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// El evento de pago del alta publica la marca de referido declarado que guardó el pago, para que Growth reintente
/// (en vez de descartar) si la atribución todavía no llegó.
/// </summary>
public sealed class OnboardingReferralDeclaredTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void The_succeeded_event_carries_the_stored_mark(bool? referralDeclared)
    {
        var message = SaaSPaymentResultMessages.OnboardingResultOf(SucceededOnboarding(referralDeclared), "corr");

        var succeeded = Assert.IsType<OnboardingPaymentSucceededIntegrationEvent>(message);
        Assert.Equal(referralDeclared, succeeded.ReferralDeclared);
    }

    private static SaaSPaymentSnapshot SucceededOnboarding(bool? referralDeclared) =>
        new(
            Guid.NewGuid(),
            Guid.Empty,
            SaaSPaymentType.OnboardingInitial,
            PaymentStatus.Succeeded,
            Guid.NewGuid(),
            "onboarding-checkout-referral",
            4900,
            "USD",
            PaymentProviderCode.Stripe,
            "pi_123",
            DateTime.UtcNow,
            null,
            null,
            null,
            1,
            true,
            Guid.Empty,
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            ReferralDeclared: referralDeclared
        );
}
