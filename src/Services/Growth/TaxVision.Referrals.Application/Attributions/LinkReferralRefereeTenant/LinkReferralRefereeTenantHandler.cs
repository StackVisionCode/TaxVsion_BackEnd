using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Referrals.Application.Attributions.LinkReferralRefereeTenant;

/// <summary>
/// Enlaza la atribución del alta (su referido es el <c>OnboardingId</c>) con la oficina real que el alta creó. El alta
/// es el tenant dueño de la atribución y debe estar en contexto. Sin atribución, el alta no vino por referido.
/// </summary>
public sealed record LinkReferralRefereeTenantCommand(
    Guid OnboardingId,
    Guid RefereeTenantId,
    Guid? RefereeUserId = null
);

public static class LinkReferralRefereeTenantHandler
{
    public static async Task<Result> Handle(
        LinkReferralRefereeTenantCommand command,
        IReferralAttributionRepository attributions,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.OnboardingId == Guid.Empty)
            return Result.Failure(new Error("ReferralAttribution.InvalidReferee", "An onboarding is required."));

        var attribution = await attributions.GetByRefereeAsync(command.OnboardingId, command.OnboardingId, ct);
        if (attribution is null)
            return Result.Success();

        var linked = attribution.LinkRefereeTenant(
            command.RefereeTenantId,
            timeProvider.GetUtcNow().UtcDateTime,
            command.RefereeUserId
        );
        if (linked.IsFailure)
            return linked;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
