using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Application.Imports.Commands;
using TaxVision.FinancialInsights.Application.Imports.Queries;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Tests.TestDoubles;

namespace TaxVision.FinancialInsights.Tests.Application;

public sealed class PortalImportBatchHandlerTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _customer = Guid.NewGuid();
    private readonly Guid _portalUser = Guid.NewGuid();

    [Fact]
    public async Task Portal_needs_the_upload_permission_and_both_modules_from_local_projections()
    {
        await using var h = new FinancialTestHarness(_tenant);
        await h.SeedTenantAsync(true, "financial_insights");
        await h.SeedCustomerAsync(_customer);

        Assert.Equal("Financial.PermissionDenied", (await Create(h, false, Guid.NewGuid())).Error.Code);
        Assert.Equal("Financial.ModuleUnavailable", (await Create(h, true, Guid.NewGuid())).Error.Code);
        Assert.Equal(0, h.Files.Calls);
    }

    [Fact]
    public async Task Pending_scan_waits_and_a_scan_finished_before_commit_is_picked_up()
    {
        await using var h = await ReadyHarness();
        var ready = Guid.NewGuid();
        var racing = Guid.NewGuid();
        var waiting = Guid.NewGuid();
        h.Files.Set(racing, ImportFileState.AwaitingScan, ImportFileState.Ready);
        h.Files.Set(waiting, ImportFileState.AwaitingScan);

        var result = await Create(h, true, ready, racing, waiting);

        Assert.True(result.IsSuccess);
        var stored = await h.Db.ImportDocuments.IgnoreQueryFilters().ToDictionaryAsync(d => d.FileId, d => d.Status);
        Assert.Equal(ImportDocumentStatus.Ready, stored[ready]);
        Assert.Equal(ImportDocumentStatus.Ready, stored[racing]);
        Assert.Equal(ImportDocumentStatus.AwaitingScan, stored[waiting]);
    }

    [Fact]
    public async Task A_file_that_fails_after_registration_is_rejected()
    {
        await using var h = await ReadyHarness();
        var file = Guid.NewGuid();
        h.Files.Set(file, ImportFileState.AwaitingScan, ImportFileState.Ineligible);

        Assert.True((await Create(h, true, file)).IsSuccess);

        var document = await h.Db.ImportDocuments.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(ImportDocumentStatus.Rejected, document.Status);
        Assert.Equal("FileNotEligible", document.FailureReason);
    }

    [Fact]
    public async Task Inactive_customer_cannot_import_or_read()
    {
        await using var h = new FinancialTestHarness(_tenant);
        await h.SeedTenantAsync();
        await h.SeedCustomerAsync(_customer, active: false);

        Assert.Equal("Import.CustomerNotFound", (await Create(h, true, Guid.NewGuid())).Error.Code);
        var list = await ListPortalImportBatchesHandler.Handle(
            new ListPortalImportBatchesQuery(_tenant, _customer, 1, 20),
            h.Batches,
            h.Access,
            CancellationToken.None
        );
        Assert.Equal("Import.CustomerNotFound", list.Error.Code);
    }

    [Fact]
    public async Task Portal_reads_only_its_own_batches_newest_first()
    {
        await using var h = await ReadyHarness();
        var other = Guid.NewGuid();
        await h.SeedCustomerAsync(other);
        var first = (await Create(h, true, Guid.NewGuid())).Value;
        await Task.Delay(30);
        var second = (await Create(h, true, Guid.NewGuid())).Value;
        await CreatePortalImportBatchHandler.Handle(
            new CreatePortalImportBatchCommand(_tenant, Guid.NewGuid(), other, [Guid.NewGuid()], true),
            h.Batches,
            h.Access,
            h.Entitlements,
            h.Files,
            h.Db,
            CancellationToken.None
        );

        var page = (
            await ListPortalImportBatchesHandler.Handle(
                new ListPortalImportBatchesQuery(_tenant, _customer, 1, 20),
                h.Batches,
                h.Access,
                CancellationToken.None
            )
        ).Value;

        Assert.Equal([second.Id, first.Id], page.Items.Select(i => i.Id).ToArray());
        Assert.Equal(2, page.TotalCount);

        var own = await GetPortalImportBatchHandler.Handle(
            new GetPortalImportBatchQuery(_tenant, _customer, first.Id),
            h.Batches,
            h.Access,
            CancellationToken.None
        );
        Assert.True(own.IsSuccess);
        var foreign = await GetPortalImportBatchHandler.Handle(
            new GetPortalImportBatchQuery(_tenant, other, first.Id),
            h.Batches,
            h.Access,
            CancellationToken.None
        );
        Assert.Equal("Import.BatchNotFound", foreign.Error.Code);
    }

    private async Task<FinancialTestHarness> ReadyHarness()
    {
        var h = new FinancialTestHarness(_tenant);
        await h.SeedTenantAsync();
        await h.SeedCustomerAsync(_customer);
        return h;
    }

    private Task<BuildingBlocks.Results.Result<TaxVision.FinancialInsights.Application.Imports.ImportBatchView>> Create(
        FinancialTestHarness h,
        bool canUpload,
        params Guid[] files
    ) =>
        CreatePortalImportBatchHandler.Handle(
            new CreatePortalImportBatchCommand(_tenant, _portalUser, _customer, files, canUpload),
            h.Batches,
            h.Access,
            h.Entitlements,
            h.Files,
            h.Db,
            CancellationToken.None
        );
}
