using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Application.Imports.Commands;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Tests.TestDoubles;

namespace TaxVision.FinancialInsights.Tests.Application;

public sealed class CreateImportBatchHandlerTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _customer = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    [Fact]
    public async Task Assigned_staff_registers_ready_documents_from_local_projections()
    {
        await using var h = await ReadyHarness();
        await h.AssignAsync(_customer, _user);

        var result = await Create(h, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.All(result.Value.Documents, d => Assert.Equal(ImportDocumentStatus.Ready, d.Status));
        Assert.Equal(_user, result.Value.CreatedBy);
        Assert.Equal(2, await h.Db.ImportDocuments.CountAsync());
        Assert.Equal(0, h.Snapshots.Calls);
    }

    [Fact]
    public async Task Unassigned_customer_is_hidden_unless_the_user_sees_all_customers()
    {
        await using var h = await ReadyHarness();

        Assert.Equal("Import.CustomerNotFound", (await Create(h, Guid.NewGuid())).Error.Code);
        Assert.True((await Create(h, [Guid.NewGuid()], canViewAll: true)).IsSuccess);
    }

    [Fact]
    public async Task Visibility_can_be_turned_off_by_configuration()
    {
        await using var h = await ReadyHarness();
        h.Visibility = Options.Create(new FinancialCustomerVisibilityOptions { Enabled = false });

        Assert.True((await Create(h, Guid.NewGuid())).IsSuccess);
    }

    [Fact]
    public async Task Inactive_or_unknown_customer_and_inactive_tenant_are_refused()
    {
        await using var h = new FinancialTestHarness(_tenant);
        await h.SeedTenantAsync(active: false);
        await h.SeedCustomerAsync(_customer, active: false);

        Assert.Equal("Tenant.Inactive", (await Create(h, [Guid.NewGuid()], canViewAll: true)).Error.Code);

        await using var other = await ReadyHarness(customerActive: false);
        Assert.Equal("Import.CustomerNotFound", (await Create(other, [Guid.NewGuid()], canViewAll: true)).Error.Code);
        Assert.Equal(
            "Import.CustomerNotFound",
            (await CreateFor(other, Guid.NewGuid(), [Guid.NewGuid()], canViewAll: true)).Error.Code
        );
    }

    [Fact]
    public async Task Missing_entitlements_are_pulled_once_and_stored_with_the_monthly_quota()
    {
        await using var h = new FinancialTestHarness(_tenant);
        await h.SeedCustomerAsync(_customer);
        h.Snapshots.Snapshot = new TenantEntitlementsSnapshot("enterprise", 7, ["financial_insights"], 150);

        var result = await Create(h, [Guid.NewGuid()], canViewAll: true);

        Assert.True(result.IsSuccess);
        var row = await h.Db.TenantEntitlementsProjections.SingleAsync();
        Assert.Equal(7, row.RevisionNumber);
        Assert.Equal(150, row.DocumentsPerMonth);
        Assert.Contains(_tenant, h.Cache.Invalidated);

        Assert.True((await Create(h, [Guid.NewGuid()], canViewAll: true)).IsSuccess);
        Assert.Equal(1, h.Snapshots.Calls);
    }

    [Fact]
    public async Task Without_entitlements_the_import_is_not_accepted_for_a_worker_that_would_reject_it()
    {
        await using var h = new FinancialTestHarness(_tenant);
        await h.SeedCustomerAsync(_customer);

        var result = await Create(h, [Guid.NewGuid()], canViewAll: true);

        Assert.Equal("Import.EntitlementsUnavailable", result.Error.Code);
        Assert.Empty(await h.Db.ImportBatches.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Module_disabled_in_the_projection_is_refused()
    {
        await using var h = new FinancialTestHarness(_tenant);
        await h.SeedTenantAsync(true, "documents");
        await h.SeedCustomerAsync(_customer);

        Assert.Equal("Financial.ModuleUnavailable", (await Create(h, [Guid.NewGuid()], canViewAll: true)).Error.Code);
    }

    [Fact]
    public async Task Files_must_be_available_and_not_registered_elsewhere()
    {
        await using var h = await ReadyHarness();
        var pending = Guid.NewGuid();
        var down = Guid.NewGuid();
        h.Files.Set(pending, ImportFileState.AwaitingScan);
        h.Files.Set(down, ImportFileState.Unavailable);

        var notEligible = await Create(h, [pending], canViewAll: true);
        Assert.Equal("Import.FileNotEligible", notEligible.Error.Code);
        Assert.Contains(pending.ToString(), notEligible.Error.Message);
        Assert.Equal("Import.FileStatusUnavailable", (await Create(h, [down], canViewAll: true)).Error.Code);

        var file = Guid.NewGuid();
        Assert.True((await Create(h, [file], canViewAll: true)).IsSuccess);
        Assert.Equal("Import.FileAlreadyRegistered", (await Create(h, [file], canViewAll: true)).Error.Code);
    }

    [Fact]
    public async Task Invalid_batch_is_rejected_before_any_upstream_call()
    {
        await using var h = await ReadyHarness();
        var file = Guid.NewGuid();

        Assert.Equal("Import.InvalidBatch", (await Create(h, [file, file], canViewAll: true)).Error.Code);
        Assert.Equal(0, h.Files.Calls);
    }

    private async Task<FinancialTestHarness> ReadyHarness(bool customerActive = true)
    {
        var h = new FinancialTestHarness(_tenant);
        await h.SeedTenantAsync();
        await h.SeedCustomerAsync(_customer, customerActive);
        return h;
    }

    private Task<BuildingBlocks.Results.Result<ImportBatchView>> Create(FinancialTestHarness h, params Guid[] files) =>
        Create(h, files, canViewAll: false);

    private Task<BuildingBlocks.Results.Result<ImportBatchView>> Create(
        FinancialTestHarness h,
        Guid[] files,
        bool canViewAll
    ) => CreateFor(h, _customer, files, canViewAll);

    private Task<BuildingBlocks.Results.Result<ImportBatchView>> CreateFor(
        FinancialTestHarness h,
        Guid customer,
        Guid[] files,
        bool canViewAll
    ) =>
        CreateImportBatchHandler.Handle(
            new CreateImportBatchCommand(_tenant, _user, customer, files, canViewAll),
            h.Batches,
            h.Access,
            h.Entitlements,
            h.Files,
            h.Visibility,
            CancellationToken.None
        );
}
