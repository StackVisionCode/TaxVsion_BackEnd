using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Application.Imports.Commands;
using TaxVision.FinancialInsights.Application.Imports.Processing;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Imports.Events;
using TaxVision.FinancialInsights.Tests.TestDoubles;

namespace TaxVision.FinancialInsights.Tests.Application;

public sealed class ImportProcessingCommandTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _customer = Guid.NewGuid();

    [Fact]
    public async Task Completion_keeps_valid_suggestions_marks_review_and_records_usage_once()
    {
        await using var h = new FinancialTestHarness(_tenant);
        var (batch, document) = await Claimed(h);
        h.Db.AiProcessingReservations.Add(AiProcessingReservation.Create(_tenant, document, "m", DateTime.UtcNow).Value);
        await h.Db.SaveChangesAsync();

        var result = await Complete(
            h,
            document,
            [
                new SuggestedMovementInput("2026-01-02", "Deposit", 100, "USD", "credit", "Income", 1),
                new SuggestedMovementInput("02/01/2026", "Bad date", 5, "USD", "debit", "Fees", 1),
                new SuggestedMovementInput("2026-01-03", "Beyond pages", 5, "USD", "debit", "Fees", 9),
            ],
            needsReview: false
        );

        Assert.Equal(ImportDocumentStatus.NeedsReview, result.Value);
        Assert.Equal("Deposit", (await h.Db.FinancialMovements.SingleAsync()).Description);
        var reservation = await h.Db.AiProcessingReservations.SingleAsync();
        Assert.Equal(120, reservation.InputTokens);
        Assert.NotNull(reservation.CompletedAtUtc);
        Assert.IsType<ImportBatchCompletedDomainEvent>(Assert.Single(batch.DomainEvents));
    }

    [Fact]
    public async Task A_document_no_longer_processing_is_left_untouched()
    {
        await using var h = new FinancialTestHarness(_tenant);
        var (_, document) = await Claimed(h);
        await Stop(h, document, ImportDocumentStop.Fail, "Interrupted");

        var late = await Complete(
            h,
            document,
            [new SuggestedMovementInput("2026-01-02", "Deposit", 100, "USD", "credit", "Income", 1)],
            needsReview: false
        );

        Assert.Equal("Import.InvalidTransition", late.Error.Code);
        Assert.Empty(await h.Db.FinancialMovements.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Stops_go_through_the_aggregate()
    {
        await using var h = new FinancialTestHarness(_tenant);
        var (batch, document) = await Claimed(h);

        Assert.Equal(ImportDocumentStatus.Ready, (await Stop(h, document, ImportDocumentStop.ReturnToQueue, "EntitlementsPending")).Value);
        batch.StartProcessing(document);
        Assert.Equal(ImportDocumentStatus.NeedsReview, (await Stop(h, document, ImportDocumentStop.HoldForReview, "AccessChanged")).Value);
        Assert.Equal("Import.DocumentNotFound", (await Stop(h, Guid.NewGuid(), ImportDocumentStop.Fail, "x")).Error.Code);
    }

    [Fact]
    public async Task Portal_batches_remember_their_source()
    {
        await using var h = new FinancialTestHarness(_tenant);
        await h.SeedTenantAsync();
        await h.SeedCustomerAsync(_customer);

        var created = await CreatePortalImportBatchHandler.Handle(
            new CreatePortalImportBatchCommand(_tenant, Guid.NewGuid(), _customer, [Guid.NewGuid()], true),
            h.Batches,
            h.Access,
            h.Entitlements,
            h.Files,
            h.Db,
            CancellationToken.None
        );

        Assert.Equal(ImportBatchSource.Portal, (await h.Db.ImportBatches.IgnoreQueryFilters().SingleAsync(b => b.Id == created.Value.Id)).Source);
    }

    private async Task<(ImportBatch Batch, Guid Document)> Claimed(FinancialTestHarness h)
    {
        var batch = ImportBatch.Create(_tenant, _customer, Guid.NewGuid(), [Guid.NewGuid()]).Value;
        var document = batch.Documents[0].Id;
        batch.MarkDocumentReady(document);
        batch.StartProcessing(document);
        await h.Batches.TryAddAsync(batch, CancellationToken.None);
        return (batch, document);
    }

    private Task<BuildingBlocks.Results.Result<ImportDocumentStatus>> Complete(
        FinancialTestHarness h,
        Guid document,
        IReadOnlyList<SuggestedMovementInput> movements,
        bool needsReview
    ) =>
        CompleteImportDocumentHandler.Handle(
            new CompleteImportDocumentCommand(_tenant, document, movements, needsReview, PageCount: 2, InputTokens: 120, OutputTokens: 40),
            h.Batches,
            h.Movements,
            h.Reservations,
            h.Db,
            CancellationToken.None
        );

    private Task<BuildingBlocks.Results.Result<ImportDocumentStatus>> Stop(
        FinancialTestHarness h,
        Guid document,
        ImportDocumentStop stop,
        string reason
    ) =>
        StopImportDocumentHandler.Handle(
            new StopImportDocumentCommand(_tenant, document, stop, reason),
            h.Batches,
            h.Db,
            CancellationToken.None
        );
}
