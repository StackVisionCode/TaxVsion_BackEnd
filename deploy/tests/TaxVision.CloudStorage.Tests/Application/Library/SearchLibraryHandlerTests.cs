using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Application.Library.Queries;
using TaxVision.CloudStorage.Domain.Files;
using static TaxVision.CloudStorage.Tests.Application.Library.FakeLibraryReadRepository;

namespace TaxVision.CloudStorage.Tests.Application.Library;

public sealed class SearchLibraryHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task Without_readable_sources_the_result_is_empty_and_the_repository_is_not_touched()
    {
        var repository = new FakeLibraryReadRepository();

        var response = await Search(repository, LibraryAccessPolicyTests.Access(readable: []));

        Assert.Empty(response.Items);
        Assert.Null(response.NextCursor);
        Assert.Equal(0, response.Counts!.Total);
        Assert.Empty(response.Counts.BySource);
        Assert.Empty(response.UnavailableSources);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Counts_list_only_readable_sources_including_those_with_no_rows()
    {
        var repository = new FakeLibraryReadRepository()
            .Add(Tenant, Row(OwnerType.Customer, FolderType.Documents, Guid.NewGuid()))
            .Add(Tenant, Row(OwnerType.Customer, FolderType.Documents, Guid.NewGuid()))
            .Add(Tenant, Row(OwnerType.Invoice, FolderType.Invoices, Guid.NewGuid(), "invoice", "invoice_pdf"));
        var access = LibraryAccessPolicyTests.Access(readable: [LibrarySource.Documents, LibrarySource.Signature]);

        var response = await Search(repository, access);

        Assert.Equal(
            [new LibrarySourceCount("documents", 2), new LibrarySourceCount("signature", 0)],
            response.Counts!.BySource
        );
        Assert.Equal(2, response.Counts.Total);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task Counts_ignore_the_source_filter()
    {
        var repository = new FakeLibraryReadRepository()
            .Add(Tenant, Row(OwnerType.Customer, FolderType.Documents, Guid.NewGuid()))
            .Add(Tenant, Row(OwnerType.Invoice, FolderType.Invoices, Guid.NewGuid(), "invoice", "invoice_pdf"));
        var access = LibraryAccessPolicyTests.Access(readable: [LibrarySource.Documents, LibrarySource.Billing]);

        var response = await Search(repository, access, new LibrarySearchRequest { Source = ["billing"] });

        Assert.Equal("billing", Assert.Single(response.Items).Source);
        Assert.Equal(2, response.Counts!.Total);
    }

    [Fact]
    public async Task No_counts_are_computed_when_not_asked()
    {
        var repository = new FakeLibraryReadRepository();

        var response = await Search(
            repository,
            LibraryAccessPolicyTests.Access(),
            new LibrarySearchRequest { Counts = false }
        );

        Assert.Null(response.Counts);
    }

    [Fact]
    public async Task Without_the_download_permission_rows_cannot_be_previewed_or_downloaded()
    {
        var repository = new FakeLibraryReadRepository().Add(Tenant, Row(OwnerType.Tenant, FolderType.Documents));
        var access = LibraryAccessPolicyTests.Access() with { CanDownload = false };

        var item = Assert.Single((await Search(repository, access)).Items);

        Assert.False(item.CanPreview);
        Assert.False(item.CanDownload);
    }

    [Fact]
    public async Task Created_by_is_null_when_the_author_is_the_tenant()
    {
        var person = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var repository = new FakeLibraryReadRepository()
            .Add(Tenant, Row(OwnerType.Tenant, FolderType.Documents, createdBy: Tenant, createdAtUtc: now))
            .Add(
                Tenant,
                Row(OwnerType.Tenant, FolderType.Documents, createdBy: person, createdAtUtc: now.AddMinutes(-1))
            );

        var items = (await Search(repository, LibraryAccessPolicyTests.Access())).Items;

        Assert.Equal([null, person], items.Select(item => item.CreatedBy));
    }

    [Fact]
    public async Task A_row_carries_the_full_contract()
    {
        var customer = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        var row = Row(
            OwnerType.Invoice,
            FolderType.Invoices,
            invoice,
            "invoice",
            "invoice_pdf",
            relatedCustomerId: customer,
            resourceId: invoice
        );
        var repository = new FakeLibraryReadRepository().Add(Tenant, row);

        var item = Assert.Single((await Search(repository, LibraryAccessPolicyTests.Access())).Items);

        Assert.Equal(row.Id, item.Id);
        Assert.Equal("billing", item.Source);
        Assert.Equal("invoice_pdf", item.Kind);
        Assert.Equal(customer, item.CustomerId);
        Assert.Null(item.FolderPath);
        Assert.Null(item.ResourceTitle);
        Assert.Equal(new LibraryOpenIn("invoice", invoice, customer, null), item.OpenIn);
        Assert.True(item.CanPreview);
        Assert.True(item.CanDownload);
    }

    [Fact]
    public async Task Only_documents_carry_their_folder_path()
    {
        var folder = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var repository = new FakeLibraryReadRepository()
            .Folder(folder, "Documents/2025")
            .Add(Tenant, Row(OwnerType.Tenant, FolderType.Documents, folderId: folder, createdAtUtc: now))
            .Add(
                Tenant,
                Row(
                    OwnerType.Customer,
                    FolderType.Documents,
                    Guid.NewGuid(),
                    "financial_import_batch",
                    "statement",
                    folderId: folder,
                    createdAtUtc: now.AddMinutes(-1)
                )
            );
        var access = LibraryAccessPolicyTests.Access(viewAll: true);

        var items = (await Search(repository, access)).Items;

        Assert.Equal(["Documents/2025", null], items.Select(item => item.FolderPath));
    }

    [Fact]
    public async Task A_full_page_returns_a_cursor_after_the_last_row()
    {
        var repository = new FakeLibraryReadRepository();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 3; i++)
            repository.Add(Tenant, Row(OwnerType.Tenant, FolderType.Documents, createdAtUtc: now.AddMinutes(-i)));

        var response = await Search(
            repository,
            LibraryAccessPolicyTests.Access(),
            new LibrarySearchRequest { Take = 2 }
        );

        Assert.Equal(2, response.Items.Count);
        Assert.Equal([3], repository.RequestedTakes);
        var cursor = LibraryCursor.Decode(response.NextCursor!, LibrarySort.Newest).Value;
        Assert.Equal(response.Items[^1].Id, cursor.Id);
    }

    [Fact]
    public async Task The_last_page_has_no_cursor()
    {
        var repository = new FakeLibraryReadRepository().Add(Tenant, Row(OwnerType.Tenant, FolderType.Documents));

        var response = await Search(
            repository,
            LibraryAccessPolicyTests.Access(),
            new LibrarySearchRequest { Take = 1 }
        );

        Assert.Single(response.Items);
        Assert.Null(response.NextCursor);
    }

    private static Task<LibrarySearchResponse> Search(
        FakeLibraryReadRepository repository,
        LibraryAccess access,
        LibrarySearchRequest? request = null
    ) =>
        SearchLibraryHandler.Handle(
            new SearchLibraryQuery(Tenant, access, LibraryFilter.Create(request ?? new LibrarySearchRequest()).Value),
            repository,
            new FakeFileOwnerVisibilityClient(),
            Options.Create(new LibraryOwnerOptions()),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );
}
