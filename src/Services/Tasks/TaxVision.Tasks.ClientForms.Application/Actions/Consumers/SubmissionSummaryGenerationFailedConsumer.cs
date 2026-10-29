using BuildingBlocks.Common;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

/// <summary>
/// Documents reportó que la generación del Submission Summary falló de forma terminal. Mismo filtrado y
/// correlación que el consumer de Completed; marca la revisión en PdfStatus=Failed para que el drawer
/// ofrezca "Retry".
/// </summary>
public static class SubmissionSummaryGenerationFailedConsumer
{
    public static async Task Handle(
        DocumentGenerationFailedIntegrationEvent evt,
        IFormRequestRepository requests,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<DocumentGenerationFailedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        if (
            !SubmissionSummaryResultSupport.IsSummary(evt.DocumentType)
            || evt.ExternalReferenceId is not { } generationId
        )
            return;

        using (correlation.Push(SubmissionSummaryResultSupport.CorrelationOf(evt.CorrelationId, evt.EventId)))
        {
            var request = await SubmissionSummaryResultSupport.LoadAsync(
                generationId,
                evt.TenantId,
                requests,
                logger,
                ct
            );
            if (request is null)
                return;
            var failed = request.MarkPdfFailed(generationId);
            if (failed.IsFailure)
                return;
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
