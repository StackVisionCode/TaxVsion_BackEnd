using BuildingBlocks.Tenancy;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Domain;

/// <summary>
/// El PDF del recibo puede estar guardado bajo otro tenant que el del pago: el recibo de un alta lo genera Auth
/// antes de que exista la oficina y queda bajo la plataforma. El pago recuerda de quién es el archivo para pedirlo
/// con ese dueño.
/// </summary>
public sealed class SaaSPaymentReceiptOwnerTests
{
    private static readonly Guid OfficeId = Guid.NewGuid();

    [Fact]
    public void A_receipt_without_an_owner_belongs_to_the_payment_tenant()
    {
        var payment = OfficePayment();

        payment.AttachReceipt(Guid.NewGuid(), DateTime.UtcNow);

        Assert.Equal(OfficeId, payment.ReceiptOwnerTenant());
    }

    [Fact]
    public void An_onboarding_receipt_from_auth_belongs_to_the_platform()
    {
        var payment = OfficePayment();

        payment.AttachReceipt(Guid.NewGuid(), DateTime.UtcNow, PlatformTenant.Id);

        Assert.Equal(PlatformTenant.Id, payment.ReceiptOwnerTenant());
    }

    // Recibos ya colgados sin dueño: volver a colgar el mismo archivo con su dueño lo corrige.
    [Fact]
    public void Attaching_the_same_file_with_its_owner_fixes_the_owner()
    {
        var payment = OfficePayment();
        var fileId = Guid.NewGuid();
        payment.AttachReceipt(fileId, DateTime.UtcNow);

        payment.AttachReceipt(fileId, DateTime.UtcNow, PlatformTenant.Id);

        Assert.Equal(fileId, payment.ReceiptFileId);
        Assert.Equal(PlatformTenant.Id, payment.ReceiptOwnerTenant());
    }

    private static SaaSPayment OfficePayment() =>
        SaaSPayment
            .Create(
                OfficeId,
                IdempotencyKey.Create($"seat-{Guid.NewGuid():N}").Value,
                Money.Create(1500, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SEATS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
}
