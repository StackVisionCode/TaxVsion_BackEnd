using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Servicio append-only de la cadena de audit. Encapsula:
/// <list type="number">
///   <item>Obtener el último sequence + hash previos.</item>
///   <item>Serializar el payload al JSON canónico.</item>
///   <item>Descifrar el <c>TenantAuditSecret</c> y computar el HMAC.</item>
///   <item>Guardar el <see cref="SignatureAuditEvent"/>.</item>
/// </list>
///
/// <para>Los consumers/handlers llaman <see cref="AppendAsync"/> con el kind + payload; el
/// resto lo maneja el servicio. Ni Application ni Domain tocan crypto.</para>
/// </summary>
public interface IAuditChainAppender
{
    Task<Result<SignatureAuditEvent>> AppendAsync(
        Guid tenantId,
        Guid signatureRequestId,
        SignatureAuditEventKind kind,
        DateTime occurredAtUtc,
        object payload,
        CancellationToken ct = default
    );
}
