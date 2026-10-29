using BuildingBlocks.Messaging.SignatureIntegrationEvents;

namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>
/// task.signature_request_from_form_requested.v1 — comando punto a punto a la cola
/// signature-client-form-commands. Solo lleva FileId, hash esperado y los datos del firmante;
/// nunca viajan bytes del PDF. El email del firmante va en claro porque la cola es punto a punto
/// (un solo consumidor, Signature) y Signature lo necesita en claro para invitar a firmar.
/// </summary>
public sealed record SignatureRequestFromFormRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid RequestId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required SignatureExternalReference ExternalReference { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required string Title { get; init; }
    public string? Category { get; init; }
    public required IReadOnlyList<SignatureRequestFromFormDocument> Documents { get; init; }
    public required IReadOnlyList<SignatureRequestFromFormSigner> Signers { get; init; }
    public required SignatureRequestFromFormPlacement Placement { get; init; }
    public bool AutoSend { get; init; } = true;
}

/// <summary>Documento a firmar: la copia en CloudStorage y su hash esperado (gate de inmutabilidad).</summary>
public sealed record SignatureRequestFromFormDocument
{
    public required Guid FileId { get; init; }
    public required string ExpectedSha256 { get; init; }
}

/// <summary>Firmante de la solicitud. CustomerId explícito si el participante ya es cliente.</summary>
public sealed record SignatureRequestFromFormSigner
{
    public required string Email { get; init; }
    public required string FullName { get; init; }
    public string? Phone { get; init; }
    public string? Language { get; init; }
    public required string VerificationMethod { get; init; }
    public Guid? CustomerId { get; init; }
}

/// <summary>Ubicación del campo de firma (ancla normalizada 0..1 sobre el documento indicado).</summary>
public sealed record SignatureRequestFromFormPlacement
{
    public int DocumentIndex { get; init; }
    public required int Page { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double W { get; init; }
    public required double H { get; init; }
}
