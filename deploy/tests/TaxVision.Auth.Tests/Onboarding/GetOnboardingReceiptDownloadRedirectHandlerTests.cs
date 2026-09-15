using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Application.Onboarding.ReceiptDownload.Queries;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>El endpoint público de descarga solo resuelve un FileId que sea un recibo realmente
/// emitido: un GUID desconocido devuelve 404 sin llegar a CloudStorage (anti-enumeración).</summary>
public sealed class GetOnboardingReceiptDownloadRedirectHandlerTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task Rejects_an_unknown_receipt_file_without_calling_cloud_storage()
    {
        var onboardings = new FakeTenantOnboardingRepository();
        var cloudStorage = new FakeCloudStorageDownloadUrlClient(Result.Success(new Uri("https://example/never")));

        var result = await GetOnboardingReceiptDownloadRedirectHandler.Handle(
            new GetOnboardingReceiptDownloadRedirectQuery(Guid.NewGuid()),
            onboardings,
            cloudStorage,
            NullLogger<GetOnboardingReceiptDownloadRedirectQuery>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.ReceiptNotFound", result.Error.Code);
        Assert.Null(cloudStorage.LastFileId);
    }

    [Fact]
    public async Task Returns_the_presigned_url_for_a_known_receipt_file()
    {
        var fileId = Guid.NewGuid();
        var onboarding = CreateOnboardingWithReceipt(fileId);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var url = new Uri("https://storage/presigned");
        var cloudStorage = new FakeCloudStorageDownloadUrlClient(Result.Success(url));

        var result = await GetOnboardingReceiptDownloadRedirectHandler.Handle(
            new GetOnboardingReceiptDownloadRedirectQuery(fileId),
            onboardings,
            cloudStorage,
            NullLogger<GetOnboardingReceiptDownloadRedirectQuery>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(url, result.Value);
        Assert.Equal(fileId, cloudStorage.LastFileId);
    }

    private static TenantOnboarding CreateOnboardingWithReceipt(Guid receiptFileId)
    {
        var onboarding = TenantOnboarding
            .Create("owner@castillotax.com", Now, Guid.NewGuid(), "Carlos", "Castillo", null, Now)
            .Value;
        onboarding.SetReceiptFileId(receiptFileId);
        return onboarding;
    }
}
