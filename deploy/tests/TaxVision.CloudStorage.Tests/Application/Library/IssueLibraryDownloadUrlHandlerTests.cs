using System.Reflection;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Api.Controllers;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Application.Library.Queries;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application.Library;

public sealed class IssueLibraryDownloadUrlHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private readonly FakeFileObjectRepository _files = new();
    private readonly FakeLibraryReadRepository _library = new();
    private readonly List<IssueDownloadUrlQuery> _delegated = [];

    [Fact]
    public async Task A_file_of_a_readable_source_gets_a_download_url_from_the_existing_handler()
    {
        var file = Seed(OwnerType.Customer, FolderType.Documents, Guid.NewGuid());

        var result = await Issue(file, LibraryAccessPolicyTests.Access(viewAll: true));

        Assert.True(result.IsSuccess);
        Assert.Equal(file.Id, result.Value.FileId);
        var delegated = Assert.Single(_delegated);
        Assert.False(delegated.ForPreview);
        Assert.Equal(file.Id, delegated.FileId);
    }

    [Fact]
    public async Task An_invoice_pdf_without_invoicing_view_is_not_found()
    {
        var file = Seed(OwnerType.Invoice, FolderType.Invoices, Guid.NewGuid(), "invoice", "invoice_pdf");

        var result = await Issue(file, LibraryAccessPolicyTests.Access(readable: [LibrarySource.Documents]));

        Assert.Equal(FileErrors.NotFound, result.Error);
        Assert.Empty(_delegated);
    }

    [Fact]
    public async Task A_client_not_assigned_under_restriction_is_not_found()
    {
        var file = Seed(OwnerType.Customer, FolderType.Documents, Guid.NewGuid());
        var access = LibraryAccessPolicyTests.Access(explorerRestrict: true, assigned: [Guid.NewGuid()]);

        var result = await Issue(file, access);

        Assert.Equal(FileErrors.NotFound, result.Error);
        Assert.Empty(_delegated);
    }

    [Fact]
    public async Task A_file_outside_the_whitelist_is_not_found()
    {
        var file = Seed(OwnerType.Tenant, FolderType.Templates, null);

        var result = await Issue(file, LibraryAccessPolicyTests.Access(viewAll: true));

        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task A_file_of_another_tenant_is_not_found()
    {
        var file = Seed(OwnerType.Tenant, FolderType.Documents, null);

        var result = await Issue(file, LibraryAccessPolicyTests.Access(viewAll: true), tenantId: Guid.NewGuid());

        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Without_readable_sources_nothing_is_read()
    {
        var file = Seed(OwnerType.Tenant, FolderType.Documents, null);

        var result = await Issue(file, LibraryAccessPolicyTests.Access(readable: []));

        Assert.Equal(FileErrors.NotFound, result.Error);
        Assert.Equal(0, _library.Calls);
    }

    [Fact]
    public void The_endpoint_also_demands_the_download_permission()
    {
        var action = typeof(LibraryController).GetMethod(nameof(LibraryController.IssueDownloadUrl))!;

        var policies = action.GetCustomAttributes<HasPermissionAttribute>().Select(a => a.Policy).ToList();

        Assert.Contains(HasPermissionAttribute.PolicyPrefix + CloudStoragePermissions.FileDownload, policies);
        Assert.Contains(HasPermissionAttribute.PolicyPrefix + CloudStoragePermissions.LibraryView, policies);
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
    }

    private FileObject Seed(
        OwnerType owner,
        FolderType folder,
        Guid? ownerId,
        string? resourceType = null,
        string? role = null
    )
    {
        var tenant = Tenant;
        var key = ObjectKey.Create($"tenants/{tenant:N}/{owner}/files/2025/{Guid.NewGuid():N}.pdf").Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenant,
                owner,
                ownerId,
                folder,
                2025,
                key,
                "doc.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(1)
            )
            .Value;
        if (resourceType is not null)
            file.DeclareSource(
                FileSourceDeclaration.Create("billing", resourceType, Guid.NewGuid(), role, null).Value,
                DateTime.UtcNow
            );
        file.MarkPendingScan(DateTime.UtcNow);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", DateTime.UtcNow);
        _files.Seed(file);
        _library.Add(
            tenant,
            new LibraryRow(
                file.Id,
                file.OriginalName,
                "application/pdf",
                file.SizeBytes,
                file.CreatedAtUtc,
                file.CreatedBy,
                file.TaxYear,
                file.OwnerType,
                file.OwnerId,
                file.FolderType,
                file.FolderId,
                file.SourceResourceType,
                file.SourceResourceId,
                file.SourceRole,
                file.RelatedCustomerId
            )
        );
        return file;
    }

    private Task<Result<DownloadUrlResponse>> Issue(FileObject file, LibraryAccess access, Guid? tenantId = null)
    {
        var bus = new FakeMessageBus { OnInvokeWithResult = DelegateToExistingHandler };
        return IssueLibraryDownloadUrlHandler.Handle(
            new IssueLibraryDownloadUrlQuery(
                tenantId ?? file.TenantId,
                Guid.NewGuid(),
                access,
                new StorageActorScope(false, null),
                file.Id,
                new RequestAuditContext(null, null, Guid.NewGuid().ToString())
            ),
            _library,
            new FakeFileOwnerVisibilityClient(),
            bus,
            CancellationToken.None
        );
    }

    private async Task<object?> DelegateToExistingHandler(object message)
    {
        var query = (IssueDownloadUrlQuery)message;
        _delegated.Add(query);
        return await IssueDownloadUrlHandler.Handle(
            query,
            _files,
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
