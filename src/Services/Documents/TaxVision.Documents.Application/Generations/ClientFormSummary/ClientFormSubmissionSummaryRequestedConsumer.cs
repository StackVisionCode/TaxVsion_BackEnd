using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace TaxVision.Documents.Application.Generations.ClientFormSummary;

/// <summary>
/// Tasks pidió el Submission Summary de una revisión completada (cola documents-client-form-summary).
/// Descifra en memoria sólo para verificar que el sobre y el payload coinciden (tenant, cliente y
/// request); la generación sigue llevando el payload cifrado, que se abre recién al renderizar.
/// Patrón: SaaSPaymentSucceededReceiptConsumer.
/// </summary>
public static class ClientFormSubmissionSummaryRequestedConsumer
{
    public const string DocumentType = "client_form.submission_summary";
    public const string TemplateKey = "client_form.submission_summary.v1";
    public const string SourceService = "tasks";

    public static async Task Handle(
        FormSubmissionSummaryRequestedIntegrationEvent evt,
        IProtectedPayloadCodec codec,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<FormSubmissionSummaryRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.GenerationId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var payload = Unprotect(evt, codec, logger);
            if (payload is null)
                return; // Descifrado o verificación fallida: reintentar no lo arregla.

            var result = await bus.InvokeAsync<Result<GenerateClientFormSummaryDocumentResult>>(
                new GenerateClientFormSummaryDocumentCommand(
                    evt.GenerationId,
                    evt.TenantId,
                    evt.FormRequestId,
                    evt.RevisionNumber,
                    evt.Participant,
                    evt.TaxYear,
                    evt.IdempotencyKey,
                    correlationId,
                    evt.Payload,
                    evt.SignatureCopy
                ),
                ct
            );

            if (result.IsFailure)
                logger.LogWarning(
                    "Client form summary generation {GenerationId} could not be requested: {Code}.",
                    evt.GenerationId,
                    result.Error.Code
                );
        }
    }

    private static SubmissionSummaryPayload? Unprotect(
        FormSubmissionSummaryRequestedIntegrationEvent evt,
        IProtectedPayloadCodec codec,
        ILogger logger
    )
    {
        try
        {
            var binding = new ProtectedPayloadBinding(
                evt.TenantId,
                evt.FormRequestId,
                evt.Participant,
                IdempotencyKey: evt.IdempotencyKey
            );
            var payload = codec.Unprotect<SubmissionSummaryPayload>(
                evt.Payload,
                ClientFormProtectedPayloadSchemas.SubmissionSummary,
                binding
            );

            // Defensa en profundidad: el contenido debe decir el mismo tenant, cliente y request que el sobre.
            if (
                payload.TenantId != evt.TenantId
                || payload.FormRequestId != evt.FormRequestId
                || payload.CustomerId != evt.Participant.CustomerId
            )
                throw new CryptographicException("Payload binding does not match the envelope.");

            return payload;
        }
        catch (CryptographicException)
        {
            logger.LogWarning(
                "Security: discarded client form summary {GenerationId} for tenant {TenantId}; protected payload validation failed.",
                evt.GenerationId,
                evt.TenantId
            );
            return null;
        }
    }
}
