using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

internal static class PublicFormChallengeVerification
{
    // Carga el último código del origen; null si no hay o si el origen sigue bloqueado.
    internal static async Task<FormAccessChallenge?> LoadVerifiableAsync(
        Guid tenantId,
        Guid? grantId,
        Guid? intakeLinkId,
        string email,
        DateTime nowUtc,
        IFormAccessChallengeRepository challenges,
        IFormExternalAccessSecurity security,
        CancellationToken ct
    )
    {
        var lookups = security.EmailLookupHashes(tenantId, email);
        if (await challenges.HasActiveBlockAsync(tenantId, grantId, intakeLinkId, lookups, nowUtc, ct))
            return null;
        return await challenges.GetLatestAsync(tenantId, grantId, intakeLinkId, lookups, ct);
    }

    // Un fallo se persiste para que cuente; si el guardado choca con otro intento, sigue siendo fallo.
    internal static async Task<bool> VerifyAsync(
        FormAccessChallenge challenge,
        bool codeMatches,
        DateTime nowUtc,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (challenge.Verify(codeMatches, nowUtc).IsSuccess)
            return true;
        await TrySaveAsync(unitOfWork, ct);
        return false;
    }

    // RowVersion del código: un intento paralelo que pierde la carrera nunca se convierte en éxito.
    internal static async Task<bool> TrySaveAsync(IUnitOfWork unitOfWork, CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }
        catch (ConflictException)
        {
            return false;
        }
    }
}
