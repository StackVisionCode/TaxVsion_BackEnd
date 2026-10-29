namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>
/// task.form_submission_summary_requested.v1 — punto a punto a la cola documents-client-form-summary.
/// El sobre lleva sólo identificadores; el snapshot va cifrado en <see cref="Payload"/> (schema
/// client-form-summary.v1), ligado a tenant, cliente y request.
/// </summary>
public sealed record FormSubmissionSummaryRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid GenerationId { get; init; }
    public required Guid FormRequestId { get; init; }
    public required int RevisionNumber { get; init; }
    public required ClientFormParticipant Participant { get; init; }
    public int? TaxYear { get; init; }
    public required string IdempotencyKey { get; init; }
    public required ProtectedPayload Payload { get; init; }

    /// <summary>P-18: si viene, Documents genera el PDF una vez y publica una segunda copia
    /// (owner/carpeta de firma) con su propio FileId, y devuelve Copies[] + SignatureAnchor.</summary>
    public SignatureCopyRequest? SignatureCopy { get; init; }
}

/// <summary>Copia del resumen destinada a Signature (P-18). Aditivo.</summary>
public sealed record SignatureCopyRequest
{
    public required string OwnerType { get; init; }
    public required string FolderType { get; init; }
    public int? TaxYear { get; init; }
}
