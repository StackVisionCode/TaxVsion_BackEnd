using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Messaging;

namespace TaxVision.Signature.Application.Requests.Commands.Delete;

/// <summary>
/// Borra en firme un borrador sin enviar. El dominio (<see cref="Domain.Requests.SignatureRequest.EnsureCanBeDeleted"/>)
/// impide borrar solicitudes ya enviadas o terminales — esas se cancelan, no se borran.
/// </summary>
public static class DeleteSignatureRequestHandler
{
    public static async Task<Result> Handle(
        DeleteSignatureRequestCommand cmd,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        ISignatureRequestListCacheInvalidator listCache,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return NotFound();

        var deletable = request.EnsureCanBeDeleted();
        if (deletable.IsFailure)
            return deletable;

        repository.Remove(request);
        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);
        return Result.Success();
    }

    private static Result NotFound() =>
        Result.Failure(
            new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
        );
}
