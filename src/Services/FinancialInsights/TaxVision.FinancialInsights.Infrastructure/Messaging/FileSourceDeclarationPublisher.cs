using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Infrastructure.Persistence;

namespace TaxVision.FinancialInsights.Infrastructure.Messaging;

/// <summary>Una página = una unidad con outbox de EF (BB-3).</summary>
internal sealed class FileSourceDeclarationPublisher(
    IServiceScopeFactory scopeFactory,
    ILogger<FileSourceDeclarationPublisher> logger
) : IFileSourceDeclarationPublisher
{
    public async Task<Result> PublishAsync(
        IReadOnlyList<FileSourceDeclaredIntegrationEvent> page,
        CancellationToken ct = default
    )
    {
        if (page.Count == 0)
            return Result.Success();

        var unit = await TransactionalJobUnit.RunAsync<FinancialInsightsDbContext>(
            scopeFactory,
            async (context, _) =>
            {
                foreach (var declaration in page)
                    await context.PublishAsync(declaration);
                return true;
            },
            logger,
            ct
        );
        return unit.Outcome == JobUnitOutcome.Committed
            ? Result.Success()
            : Result.Failure(
                new Error(
                    "Financial.FileSources.PublishFailed",
                    "A page of file source declarations was not published."
                )
            );
    }
}
