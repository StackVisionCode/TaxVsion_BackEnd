using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Auth genera el recibo del alta antes de que exista la oficina: el archivo es de la plataforma.</summary>
public sealed class OnboardingReceiptReadyConsumerTests
{
    [Fact]
    public async Task The_onboarding_receipt_is_attached_as_owned_by_the_platform()
    {
        var onboardingId = Guid.NewGuid();
        var receiptFileId = Guid.NewGuid();
        var payment = OnboardingPayment(onboardingId);

        await OnboardingReceiptReadyConsumer.Handle(
            new OnboardingReceiptReadyIntegrationEvent
            {
                OnboardingId = onboardingId,
                ReceiptFileId = receiptFileId,
                ReceiptDownloadUrl = "https://auth.test/onboarding/receipts/x/download",
                Email = "owner@office.test",
                FirstName = "Owner",
            },
            new FakeSaaSPaymentRepository { Onboarding = payment },
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.Equal(receiptFileId, payment.ReceiptFileId);
        Assert.Equal(PlatformTenant.Id, payment.ReceiptOwnerTenant());
    }

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
