using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Domain.Imports;

namespace TaxVision.FinancialInsights.Application.Imports.Commands;

/// <summary>Vuelve a la cola un documento fallido antes de llamar al modelo (sin costo repetido).</summary>
public sealed record RetryImportDocumentCommand(
    Guid TenantId,
    Guid UserId,
    Guid BatchId,
    Guid DocumentId,
    bool CanViewAllCustomers
);

public static class RetryImportDocumentHandler
{
    public static async Task<Result<ImportDocumentView>> Handle(
        RetryImportDocumentCommand command,
        IImportBatchRepository batches,
        IAiReservationRepository reservations,
        IFinancialAccessReader access,
        IUnitOfWork unitOfWork,
        IOptions<FinancialCustomerVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var batch = await batches.GetAsync(command.TenantId, command.BatchId, ct);
        if (batch is null)
            return Result.Failure<ImportDocumentView>(FinancialInsightsErrors.BatchNotFound);

        // Un cliente no visible responde igual que un lote inexistente.
        var customer = await ImportAccessRules.FindVisibleCustomerAsync(
            command.TenantId,
            command.UserId,
            batch.CustomerId,
            command.CanViewAllCustomers,
            visibility.Value.Enabled,
            requireActive: true,
            access,
            ct
        );
        if (customer is null)
            return Result.Failure<ImportDocumentView>(FinancialInsightsErrors.BatchNotFound);

        var document = batch.FindDocument(command.DocumentId);
        if (document is null)
            return Result.Failure<ImportDocumentView>(FinancialInsightsErrors.DocumentNotFound);

        var attempted = await reservations.ExistsForDocumentAsync(command.TenantId, document.Id, ct);
        var retried = batch.RetryDocument(document.Id, attempted);
        if (retried.IsFailure)
            return Result.Failure<ImportDocumentView>(retried.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ImportDocumentView.From(document));
    }
}
