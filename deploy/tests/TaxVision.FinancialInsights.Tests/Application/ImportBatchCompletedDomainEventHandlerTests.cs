extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using BuildingBlocks.Messaging.FinancialInsightsIntegrationEvents;
using TaxVision.FinancialInsights.Application;
using TaxVision.FinancialInsights.Application.Imports.DomainEvents;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Imports.Events;
using Wolverine;

namespace TaxVision.FinancialInsights.Tests.Application;

public sealed class ImportBatchCompletedDomainEventHandlerTests
{
    [Theory]
    [InlineData(ImportBatchSource.Staff, FinancialImportBatchSources.Staff)]
    [InlineData(ImportBatchSource.Portal, FinancialImportBatchSources.Portal)]
    public void Completion_becomes_the_integration_event_with_every_field(ImportBatchSource source, string expected)
    {
        var at = new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);
        var completed = new ImportBatchCompletedDomainEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            source,
            3,
            2,
            1,
            4,
            at
        );

        var integration = ImportBatchCompletedDomainEventHandler.Handle(
            completed,
            new Envelope { CorrelationId = "corr-1" }
        );

        Assert.Equal(completed.TenantId, integration.TenantId);
        Assert.Equal("corr-1", integration.CorrelationId);
        Assert.Equal(completed.BatchId, integration.BatchId);
        Assert.Equal(completed.CustomerId, integration.CustomerId);
        Assert.Equal(completed.CreatedBy, integration.CreatedByUserId);
        Assert.Equal(expected, integration.Source);
        Assert.Equal((3, 2, 1, 4), (integration.CompletedDocuments, integration.NeedsReviewDocuments, integration.FailedDocuments, integration.RejectedDocuments));
        Assert.Equal(at, integration.CompletedAtUtc);
        Assert.Equal(at, integration.OccurredOn);
    }
}

// Con UseTaxVisionDomainEvents() todo evento de dominio tiene handler y viaja en el sobre.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(ImportBatch).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(AssemblyMarker).Assembly];
}
