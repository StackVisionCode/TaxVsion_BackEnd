using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Referrals.Application.Attributions.FindAttributionByReferee;

/// <summary>
/// ¿Este referido (el alta aún sin oficina) llegó con un código de referido? Devuelve el Id de su atribución, o null.
/// Lo usa el consumer del pago del alta para calificar al referido sin que Auth tenga que guardar ni enviar ese Id.
/// El tenant dueño de la atribución del alta es el propio <c>OnboardingId</c> y debe estar en contexto.
/// </summary>
public sealed record FindAttributionByRefereeQuery(Guid OwnerTenantId, Guid RefereeId);

public static class FindAttributionByRefereeHandler
{
    public static async Task<Result<Guid?>> Handle(
        FindAttributionByRefereeQuery query,
        IReferralAttributionRepository attributions,
        CancellationToken ct
    )
    {
        if (query.OwnerTenantId == Guid.Empty || query.RefereeId == Guid.Empty)
            return Result.Failure<Guid?>(new Error("ReferralAttribution.InvalidReferee", "A referee is required."));

        var attribution = await attributions.GetByRefereeAsync(query.OwnerTenantId, query.RefereeId, ct);
        return Result.Success<Guid?>(attribution?.Id);
    }
}
