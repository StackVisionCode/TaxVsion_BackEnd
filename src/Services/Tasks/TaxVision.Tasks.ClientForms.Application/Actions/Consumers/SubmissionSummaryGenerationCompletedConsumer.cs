using BuildingBlocks.Common;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

/// <summary>
/// Documents terminó la generación del Submission Summary. Los eventos llegan por el fanout (cola
/// tasks-events) para TODOS los tipos de documento; se filtra por DocumentType y se correlaciona la
/// revisión por su PdfGenerationId (= ExternalReferenceId que Tasks mandó). Cross-tenant: se valida el
/// tenant contra la request encontrada antes de mutarla.
/// </summary>
public static class SubmissionSummaryGenerationCompletedConsumer
{
    public static async Task Handle(
        DocumentGenerationCompletedIntegrationEvent evt,
        IFormRequestRepository requests,
        ClientFormSignatureDispatcher signatureDispatcher,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<DocumentGenerationCompletedIntegrationEvent> logger,
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
            var linked = request.LinkPdf(generationId, evt.FileId);
            if (linked.IsFailure)
                return;

            // P-18: si vino una copia de firma, prepara el comando a Signature (muta la revisión).
            var signatureMessage = await signatureDispatcher.PrepareAsync(request, evt, ct);

            // Guardar antes de publicar: el outbox confirma el comando en la misma transacción.
            await unitOfWork.SaveChangesAsync(ct);
            if (signatureMessage is not null)
                await bus.PublishAsync(signatureMessage);
        }
    }
}

internal static class SubmissionSummaryResultSupport
{
    private const string SummaryDocumentType = "client_form.submission_summary";

    internal static bool IsSummary(string? documentType) =>
        string.Equals(documentType, SummaryDocumentType, StringComparison.Ordinal);

    internal static string CorrelationOf(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;

    internal static async Task<FormRequest?> LoadAsync(
        Guid generationId,
        Guid tenantId,
        IFormRequestRepository requests,
        ILogger logger,
        CancellationToken ct
    )
    {
        var request = await requests.GetByPdfGenerationIdAsync(generationId, ct);
        if (request is null)
            return null;
        if (request.TenantId != tenantId)
        {
            logger.LogWarning(
                "Submission summary generation {GenerationId} tenant {EventTenant} does not match request tenant {RequestTenant}; ignoring.",
                generationId,
                tenantId,
                request.TenantId
            );
            return null;
        }
        return request;
    }
}
