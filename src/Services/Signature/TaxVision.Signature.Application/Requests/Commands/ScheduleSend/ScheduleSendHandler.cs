using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Requests.Commands.ScheduleSend;

/// <summary>
/// F3 — Transiciona Draft → Scheduled validando las mismas precondiciones que un envío manual
/// (hash + firmantes + ≥1 campo). La UI valida la zona horaria antes de llamar; aquí comparamos
/// contra <c>DateTime.UtcNow</c> para rechazar programaciones en el pasado.
/// </summary>
public static class ScheduleSendHandler
{
    public static async Task<Result> Handle(
        ScheduleSendCommand cmd,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        ISignatureRequestListCacheInvalidator listCache,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return NotFound();

        var scheduled = request.ScheduleSend(cmd.ScheduledSendAtUtc, DateTime.UtcNow);
        if (scheduled.IsFailure)
            return scheduled;

        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);
        return Result.Success();
    }

    private static Result NotFound() =>
        Result.Failure(
            new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
        );
}
