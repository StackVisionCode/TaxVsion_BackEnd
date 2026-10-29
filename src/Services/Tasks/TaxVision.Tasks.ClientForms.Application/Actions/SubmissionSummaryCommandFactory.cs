using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

public static class SubmissionSummaryCommandFactory
{
    // Clave por generación: una reentrega del mismo comando (mismo GenerationId) se deduplica en Documents,
    // pero un reintento del staff (GenerationId nuevo) genera un PDF nuevo. Con la clave fija por revisión,
    // Documents descartaba el reintento de una generación ya completada y la request quedaba en Requested.
    public static string IdempotencyKeyFor(Guid requestId, int revisionNumber, Guid generationId) =>
        $"client-form:{requestId:N}:r{revisionNumber}:{generationId:N}";

    // El snapshot queda cifrado y atado a tenant, request, participante y clave: copiado a otro comando no valida.
    public static FormSubmissionSummaryRequestedIntegrationEvent Create(
        FormRequest request,
        int revisionNumber,
        Guid generationId,
        SubmissionSummarySnapshot snapshot,
        IProtectedPayloadCodec codec,
        string correlationId,
        SignatureCopyRequest? signatureCopy = null
    )
    {
        var participant = FormRequestIntegrationEventFactory.Participant(request);
        var idempotencyKey = IdempotencyKeyFor(request.Id, revisionNumber, generationId);
        var binding = new ProtectedPayloadBinding(
            request.TenantId,
            request.Id,
            participant,
            IdempotencyKey: idempotencyKey
        );
        var payload = new SubmissionSummaryPayload
        {
            TenantId = request.TenantId,
            CustomerId = request.EffectiveCustomerId,
            FormRequestId = request.Id,
            Snapshot = snapshot,
        };
        return new FormSubmissionSummaryRequestedIntegrationEvent
        {
            EventId = generationId,
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            GenerationId = generationId,
            FormRequestId = request.Id,
            RevisionNumber = revisionNumber,
            Participant = participant,
            TaxYear = request.TaxYear,
            IdempotencyKey = idempotencyKey,
            Payload = codec.Protect(ClientFormProtectedPayloadSchemas.SubmissionSummary, binding, payload),
            SignatureCopy = signatureCopy,
        };
    }
}
