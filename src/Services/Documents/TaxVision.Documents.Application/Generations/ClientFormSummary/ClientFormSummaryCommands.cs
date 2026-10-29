using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

namespace TaxVision.Documents.Application.Generations.ClientFormSummary;

// Alta de la generación del Submission Summary. El snapshot viaja SIEMPRE cifrado (Payload); se
// descifra sólo al renderizar. GenerationId es el id que Tasks guarda en la revisión (correlación).
public sealed record GenerateClientFormSummaryDocumentCommand(
    Guid GenerationId,
    Guid TenantId,
    Guid FormRequestId,
    int RevisionNumber,
    ClientFormParticipant Participant,
    int? TaxYear,
    string IdempotencyKey,
    string CorrelationId,
    ProtectedPayload Payload,
    // P-18: si viene, Documents genera una segunda copia (owner/carpeta de firma) y devuelve Copies + ancla.
    SignatureCopyRequest? SignatureCopy = null
);

public sealed record GenerateClientFormSummaryDocumentResult(Guid GenerationId, string Status);

// Render + subida. Lleva el payload cifrado y los ids del binding para volver a autenticarlo al abrirlo.
public sealed record ProcessClientFormSummaryGenerationCommand(
    Guid GenerationId,
    Guid TenantId,
    Guid FormRequestId,
    int RevisionNumber,
    ClientFormParticipant Participant,
    int? TaxYear,
    string IdempotencyKey,
    string CorrelationId,
    ProtectedPayload Payload,
    SignatureCopyRequest? SignatureCopy = null
);
