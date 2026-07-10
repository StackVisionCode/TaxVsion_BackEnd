using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Application.Abstractions;

public sealed record AuditChainTail(long LastSequence, string LastChainHash);

/// <summary>
/// Repositorio append-only de la cadena de audit. Expone:
/// <list type="bullet">
///   <item><see cref="GetTailAsync"/>: último sequence + hash para una solicitud (para
///     construir el siguiente evento).</item>
///   <item><see cref="AddAsync"/>: añade un evento nuevo — nunca update ni delete.</item>
///   <item><see cref="ListAsync"/>: leer la cadena completa en orden (para el verificador).</item>
/// </list>
/// </summary>
public interface ISignatureAuditRepository
{
    Task<AuditChainTail?> GetTailAsync(Guid tenantId, Guid signatureRequestId, CancellationToken ct = default);

    Task AddAsync(SignatureAuditEvent evt, CancellationToken ct = default);

    Task<IReadOnlyList<SignatureAuditEvent>> ListAsync(
        Guid tenantId,
        Guid signatureRequestId,
        CancellationToken ct = default
    );
}
