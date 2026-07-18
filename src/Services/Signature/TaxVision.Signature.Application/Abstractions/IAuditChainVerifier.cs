using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>Rango del defecto detectado si la cadena falla.</summary>
public sealed record AuditChainDefect(long Sequence, string Reason);

/// <summary>
/// Resultado de la verificación local. Si <see cref="IsIntact"/> es <c>true</c>, la cadena
/// no ha sido alterada desde su emisión (recomputando el HMAC de cada fila con el
/// <c>TenantAuditSecret</c>). Si es <c>false</c>, <see cref="Defect"/> apunta al primer
/// evento donde falló la validación.
/// </summary>
public sealed record AuditChainVerification(
    bool IsIntact,
    long EventCount,
    long LastSequence,
    AuditChainDefect? Defect
);

/// <summary>
/// Verificador de la cadena de audit. Recomputa el HMAC de cada evento contra el
/// <c>TenantAuditSecret</c> del tenant y compara con el <c>ChainHash</c> almacenado.
/// Es la contraparte de <see cref="IAuditChainAppender"/> y sirve al endpoint público
/// de verificación.
/// </summary>
public interface IAuditChainVerifier
{
    Task<AuditChainVerification> VerifyAsync(
        Guid tenantId,
        Guid signatureRequestId,
        IReadOnlyList<SignatureAuditEvent> events,
        CancellationToken ct = default
    );
}
