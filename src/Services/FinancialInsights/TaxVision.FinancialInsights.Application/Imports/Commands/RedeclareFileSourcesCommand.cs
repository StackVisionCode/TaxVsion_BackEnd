using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;

namespace TaxVision.FinancialInsights.Application.Imports.Commands;

/// <summary>Resultado del backfill: lotes revisados y archivos declarados (o por declarar en <c>DryRun</c>).</summary>
public sealed record RedeclaredFileSourcesReport(bool DryRun, int BatchesScanned, int FilesDeclared);

/// <summary>
/// Backfill: vuelve a declarar en CloudStorage el archivo de cada documento de los lotes del tenant como estado de
/// cuenta del lote. No modifica filas de FinancialInsights. Idempotente. <paramref name="DryRun"/> solo cuenta.
/// </summary>
public sealed record RedeclareFileSourcesCommand(Guid TenantId, bool DryRun);

public static class RedeclareFileSourcesHandler
{
    private const int PageSize = 500;

    public static async Task<Result<RedeclaredFileSourcesReport>> Handle(
        RedeclareFileSourcesCommand command,
        IImportBatchRepository batches,
        IFileSourceDeclarationPublisher publisher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var files = await batches.ListFilesAsync(command.TenantId, ct);
        var declarations = files
            .Select(file =>
                ImportFileSources.Declaration(
                    command.TenantId,
                    file.BatchId,
                    file.CustomerId,
                    file.FileId,
                    correlation.CorrelationId
                )
            )
            .ToList();

        if (!command.DryRun)
            foreach (var page in declarations.Chunk(PageSize))
            {
                var published = await publisher.PublishAsync(page, ct);
                if (published.IsFailure)
                    return Result.Failure<RedeclaredFileSourcesReport>(published.Error);
            }

        var batchesScanned = files.Select(file => file.BatchId).Distinct().Count();
        return Result.Success(new RedeclaredFileSourcesReport(command.DryRun, batchesScanned, declarations.Count));
    }
}
