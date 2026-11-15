using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Queries;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// La oficina pide su recibo y PaymentApp le pide la URL firmada a CloudStorage con el dueño del archivo, no con
/// el tenant de la oficina: el recibo de un alta está guardado bajo la plataforma (pedirlo con la oficina da 404).
/// </summary>
public sealed class GetReceiptDownloadUrlHandlerTests
{
    private static readonly Guid OfficeId = Guid.NewGuid();

    [Fact]
    public async Task An_onboarding_receipt_is_requested_from_the_platform()
    {
        var payment = OfficePayment();
        payment.AttachReceipt(Guid.NewGuid(), DateTime.UtcNow, PlatformTenant.Id);
        var downloads = new CapturingDownloads();

        var result = await HandleAsync(payment, downloads);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformTenant.Id, downloads.AskedTenantId);
    }

    [Fact]
    public async Task An_office_receipt_is_requested_from_the_office()
    {
        var payment = OfficePayment();
        payment.AttachReceipt(Guid.NewGuid(), DateTime.UtcNow);
        var downloads = new CapturingDownloads();

        await HandleAsync(payment, downloads);

        Assert.Equal(OfficeId, downloads.AskedTenantId);
    }

    private static Task<Result<ReceiptDownloadUrlResponse>> HandleAsync(
        SaaSPayment payment,
        CapturingDownloads downloads
    ) =>
        GetReceiptDownloadUrlHandler.Handle(
            new GetReceiptDownloadUrlQuery(OfficeId, payment.Id),
            new FakeSaaSPaymentRepository { ById = payment },
            downloads,
            CancellationToken.None
        );

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

    private sealed class CapturingDownloads : IReceiptDownloadUrlClient
    {
        public Guid? AskedTenantId { get; private set; }

        public Task<Result<ReceiptDownloadUrl>> GetAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
        {
            AskedTenantId = tenantId;
            return Task.FromResult(
                Result.Success(
                    new ReceiptDownloadUrl(new Uri("https://files.test/receipt.pdf"), DateTime.UtcNow.AddMinutes(5))
                )
            );
        }
    }
}
