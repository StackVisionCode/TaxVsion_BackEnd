using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>La URL de descarga respeta la compuerta por origen y el filtro de clientes asignados.</summary>
public sealed class IssueDownloadUrlSourceGateTests
{
    [Fact]
    public async Task Staff_without_invoicing_view_gets_not_found_for_an_invoice_pdf()
    {
        var file = Available(OwnerType.Invoice);

        var result = await IssueUrl(file, Staff(readable: []));

        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Staff_with_invoicing_view_gets_the_url()
    {
        var file = Available(OwnerType.Invoice);

        var result = await IssueUrl(file, Staff(readable: [FileReadSurface.Billing]));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Staff_not_assigned_to_the_client_gets_not_found()
    {
        var file = Available(OwnerType.Customer);
        var scope = Staff(readable: []) with { BypassAssignmentFilter = false, AssignedCustomerIds = [Guid.NewGuid()] };

        var result = await IssueUrl(file, scope);

        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    private static StorageActorScope Staff(FileReadSurface[] readable) =>
        new(false, null) { ReadableSurfaces = readable.ToHashSet() };

    private static FileObject Available(OwnerType ownerType)
    {
        var file = StorageActorScopeSourceGateTests.File(ownerType);
        file.MarkPendingScan(DateTime.UtcNow);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", DateTime.UtcNow);
        return file;
    }

    private static Task<Result<DownloadUrlResponse>> IssueUrl(FileObject file, StorageActorScope scope)
    {
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        return IssueDownloadUrlHandler.Handle(
            new IssueDownloadUrlQuery(
                file.TenantId,
                Guid.NewGuid(),
                scope,
                file.Id,
                new RequestAuditContext(null, null, Guid.NewGuid().ToString())
            ),
            files,
            new FakeStorageAuditRepository(),
            new FakeObjectStorage(),
            Options.Create(new CloudStorageOptions()),
            new FakeStoragePlatformSettingsProvider(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );
    }
}
