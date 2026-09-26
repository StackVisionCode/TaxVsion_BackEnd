using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Application.Profiles.EffectiveSignature;

/// <summary>
/// Decide qué firma se estampará por un preparador: su firma personal por defecto si el tenant lo
/// permite y existe; en caso contrario, la firma de oficina por defecto. Falla si no hay ninguna.
/// </summary>
public interface IEffectiveSignatureResolver
{
    Task<Result<SignatureProfile>> ResolveAsync(Guid tenantId, Guid preparerUserId, CancellationToken ct = default);
}
