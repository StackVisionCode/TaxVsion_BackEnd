using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Requests.Commands.SetPractitionerPin;

/// <summary>
/// Asigna el Practitioner PIN a la solicitud. Fases: (1) validar formato del PIN via VO,
/// (2) hashear con PBKDF2 (Infrastructure), (3) delegar la asignación al aggregate,
/// (4) persistir. El valor en claro nunca cruza la capa Application más allá del hasher.
/// </summary>
public static class SetPractitionerPinHandler
{
    public static async Task<Result> Handle(
        SetPractitionerPinCommand cmd,
        ISignatureRequestRepository repository,
        IPinHasher hasher,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var pinResult = PractitionerPin.Create(cmd.Pin);
        if (pinResult.IsFailure)
            return pinResult;

        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure(
                new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
            );

        var hash = hasher.Hash(pinResult.Value.Value);
        var setResult = request.SetPractitionerPin(hash, cmd.SetByUserId, DateTime.UtcNow);
        if (setResult.IsFailure)
            return setResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
