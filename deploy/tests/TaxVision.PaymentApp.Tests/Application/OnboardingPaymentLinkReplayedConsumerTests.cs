using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Onboarding.IntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// Altas viejas: su pago quedó sin oficina y sin recibo, así que la oficina no lo ve en su historial. Auth
/// vuelve a anunciar el vínculo y el pago pasa a la oficina con el recibo que Auth ya generó.
/// </summary>
public sealed class OnboardingPaymentLinkReplayedConsumerTests
{
    [Fact]
    public async Task The_payment_moves_to_its_office_and_gets_the_onboarding_receipt()
    {
        var onboardingId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var receiptFileId = Guid.NewGuid();
        var payment = OnboardingPayment(onboardingId);
        var payments = new FakeSaaSPaymentRepository { Onboarding = payment };
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(onboardingId, tenantId, receiptFileId, payments, unitOfWork);

        Assert.Equal(tenantId, payment.TenantId);
        Assert.Equal(receiptFileId, payment.ReceiptFileId);
        Assert.Equal(PlatformTenant.Id, payment.ReceiptOwnerTenant());
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    // Recibos de Auth colgados antes de saber su dueño: la descarga los pedía con la oficina y daba 404.
    [Fact]
    public async Task The_auth_receipt_already_attached_gets_the_platform_as_owner()
    {
        var onboardingId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var receiptFileId = Guid.NewGuid();
        var payment = OnboardingPayment(onboardingId);
        payment.RehomeToTenant(tenantId, DateTime.UtcNow);
        payment.AttachReceipt(receiptFileId, DateTime.UtcNow);
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(
            onboardingId,
            tenantId,
            receiptFileId,
            new FakeSaaSPaymentRepository { Onboarding = payment },
            unitOfWork
        );

        Assert.Equal(PlatformTenant.Id, payment.ReceiptOwnerTenant());
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task A_receipt_the_payment_already_has_is_not_replaced()
    {
        var onboardingId = Guid.NewGuid();
        var existingReceipt = Guid.NewGuid();
        var payment = OnboardingPayment(onboardingId);
        payment.AttachReceipt(existingReceipt, DateTime.UtcNow);
        var payments = new FakeSaaSPaymentRepository { Onboarding = payment };

        await HandleAsync(onboardingId, Guid.NewGuid(), Guid.NewGuid(), payments, new FakeUnitOfWork());

        Assert.Equal(existingReceipt, payment.ReceiptFileId);
    }

    [Fact]
    public async Task Without_a_receipt_the_payment_still_moves_to_its_office()
    {
        var onboardingId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var payment = OnboardingPayment(onboardingId);
        var payments = new FakeSaaSPaymentRepository { Onboarding = payment };

        await HandleAsync(onboardingId, tenantId, receiptFileId: null, payments, new FakeUnitOfWork());

        Assert.Equal(tenantId, payment.TenantId);
        Assert.Null(payment.ReceiptFileId);
    }

    [Fact]
    public async Task A_replay_of_an_already_linked_payment_writes_nothing()
    {
        var onboardingId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var receiptFileId = Guid.NewGuid();
        var payment = OnboardingPayment(onboardingId);
        var payments = new FakeSaaSPaymentRepository { Onboarding = payment };
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(onboardingId, tenantId, receiptFileId, payments, unitOfWork);
        await HandleAsync(onboardingId, tenantId, receiptFileId, payments, unitOfWork);

        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    // Alta cubierta al 100 % por código: no hay pago que vincular.
    [Fact]
    public async Task An_onboarding_without_a_payment_is_ignored()
    {
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new FakeSaaSPaymentRepository(), unitOfWork);

        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private static Task HandleAsync(
        Guid onboardingId,
        Guid tenantId,
        Guid? receiptFileId,
        FakeSaaSPaymentRepository payments,
        FakeUnitOfWork unitOfWork
    ) =>
        OnboardingPaymentLinkReplayedConsumer.Handle(
            new OnboardingPaymentLinkReplayedIntegrationEvent
            {
                TenantId = tenantId,
                OnboardingId = onboardingId,
                CreatedTenantId = tenantId,
                ReceiptFileId = receiptFileId,
            },
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
