using System.Globalization;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Domain.Imports;

namespace TaxVision.FinancialInsights.Application.Imports.Processing;

/// <summary>Movimiento tal como lo devolvió el modelo; se valida en el dominio antes de guardarse.</summary>
public sealed record SuggestedMovementInput(
    string TransactionDate,
    string Description,
    decimal Amount,
    string Currency,
    string Direction,
    string SuggestedCategory,
    int SourcePage
);

/// <summary>
/// Resultado del worker para un documento tomado. Una unidad por transacción: movimientos, uso del cupo y el estado
/// del lote se guardan juntos, y el aviso de cierre sale por el outbox en la misma transacción.
/// </summary>
public sealed record CompleteImportDocumentCommand(
    Guid TenantId,
    Guid DocumentId,
    IReadOnlyList<SuggestedMovementInput> Movements,
    bool NeedsReview,
    int PageCount,
    int InputTokens,
    int OutputTokens
);

public static class CompleteImportDocumentHandler
{
    public static async Task<Result<ImportDocumentStatus>> Handle(
        CompleteImportDocumentCommand command,
        IImportBatchRepository batches,
        IFinancialMovementRepository movements,
        IAiReservationRepository reservations,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var batch = await batches.GetByDocumentAsync(command.TenantId, command.DocumentId, ct);
        var document = batch?.FindDocument(command.DocumentId);
        if (batch is null || document is null)
            return Result.Failure<ImportDocumentStatus>(FinancialInsightsErrors.DocumentNotFound);

        // Una sugerencia inválida no se guarda: el documento queda para revisión.
        var needsReview = command.NeedsReview || command.Movements.Count == 0;
        var accepted = new List<FinancialMovement>();
        foreach (var suggestion in command.Movements)
        {
            if (
                !DateOnly.TryParseExact(
                    suggestion.TransactionDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date
                )
                || suggestion.SourcePage > command.PageCount
            )
            {
                needsReview = true;
                continue;
            }
            var movement = FinancialMovement.Create(
                command.TenantId,
                batch.CustomerId,
                document.Id,
                date,
                suggestion.Description,
                suggestion.Amount,
                suggestion.Currency,
                suggestion.Direction,
                suggestion.SuggestedCategory,
                suggestion.SourcePage
            );
            if (movement.IsSuccess)
                accepted.Add(movement.Value);
            else
                needsReview = true;
        }

        var completed = batch.CompleteDocument(document.Id, needsReview);
        if (completed.IsFailure)
            return Result.Failure<ImportDocumentStatus>(completed.Error);

        foreach (var movement in accepted)
            movements.Add(movement);
        var reservation = await reservations.GetForDocumentAsync(command.TenantId, document.Id, ct);
        reservation?.RecordUsage(command.InputTokens, command.OutputTokens);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(document.Status);
    }
}

public enum ImportDocumentStop
{
    ReturnToQueue,
    HoldForReview,
    Fail,
}

/// <summary>El worker detiene un documento tomado: lo devuelve a la cola, lo deja para revisión o lo marca fallido.</summary>
public sealed record StopImportDocumentCommand(Guid TenantId, Guid DocumentId, ImportDocumentStop Stop, string Reason);

public static class StopImportDocumentHandler
{
    public static async Task<Result<ImportDocumentStatus>> Handle(
        StopImportDocumentCommand command,
        IImportBatchRepository batches,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var batch = await batches.GetByDocumentAsync(command.TenantId, command.DocumentId, ct);
        var document = batch?.FindDocument(command.DocumentId);
        if (batch is null || document is null)
            return Result.Failure<ImportDocumentStatus>(FinancialInsightsErrors.DocumentNotFound);

        var stopped = command.Stop switch
        {
            ImportDocumentStop.ReturnToQueue => batch.ReturnDocumentToQueue(document.Id),
            ImportDocumentStop.HoldForReview => batch.HoldDocumentForReview(document.Id, command.Reason),
            _ => batch.FailDocument(document.Id, command.Reason),
        };
        if (stopped.IsFailure)
            return Result.Failure<ImportDocumentStatus>(stopped.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(document.Status);
    }
}
