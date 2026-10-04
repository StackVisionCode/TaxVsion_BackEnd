namespace TaxVision.Signature.Application.Requests.Commands.PreparerFields;

/// <summary>
/// Congela qué firma reutilizable se estampará por el preparador. El caller pasa el usuario para resolver
/// la firma efectiva cuando no envía un fileId explícito (usa la del preparador, o la de oficina).
/// </summary>
public sealed record SetPreparerSignatureCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid ActorUserId,
    bool ActorIsAdmin,
    // F4 — permiso signature.sign_own del actor; false = limitado a la firma de oficina.
    bool ActorHasSignOwn,
    Guid? SignatureFileId
);
