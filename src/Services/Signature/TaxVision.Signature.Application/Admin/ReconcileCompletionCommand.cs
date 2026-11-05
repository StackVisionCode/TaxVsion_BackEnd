using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Public;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Admin;

/// <summary>Resultado de la reposición: si se completó ahora y el estado final.</summary>
public sealed record ReconcileCompletionReport(Guid SignatureRequestId, bool DryRun, bool Completed, string Status);

/// <summary>
/// Reposición SIG-1: una solicitud con todos los firmantes firmados que quedó sin completar (dos firmas
/// que se pisaron) se completa y publica lo mismo que el camino normal. Idempotente: una ya completada no
/// hace nada. Las <c>Expired</c> solo con <paramref name="AllowExpired"/> (D-SIG-1). Con
/// <paramref name="DryRun"/> solo valida, sin guardar ni publicar.
/// </summary>
public sealed record ReconcileCompletionCommand(Guid TenantId, Guid SignatureRequestId, bool AllowExpired, bool DryRun);

public static class ReconcileCompletionHandler
{
    public static async Task<Result<ReconcileCompletionReport>> Handle(
        ReconcileCompletionCommand command,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISignatureRequestListCacheInvalidator listCache,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(command.TenantId, command.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure<ReconcileCompletionReport>(
                new Error("Signature.Request.NotFound", "The signature request does not exist.")
            );

        // La simulación no toca el agregado: el middleware de Wolverine guarda lo que quede modificado.
        var reconciled = command.DryRun
            ? request.EvaluateCompletionReconciliation(command.AllowExpired)
            : request.ReconcileCompletion(DateTime.UtcNow, command.AllowExpired);
        if (reconciled.IsFailure)
            return Result.Failure<ReconcileCompletionReport>(reconciled.Error);

        if (!reconciled.Value || command.DryRun)
            return Result.Success(Report(command, request, reconciled.Value));

        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(request.TenantId, ct);

        var readyDocumentIds = request
            .Documents.Where(document =>
                document.SealedFileId is null && request.IsDocumentReadyForSealing(document.Id)
            )
            .Select(document => document.Id)
            .ToList();
        if (readyDocumentIds.Count > 0)
            await SubmitSignatureHandler.PublishDocumentsReadyForSealingAsync(
                request,
                readyDocumentIds,
                request.CompletedAtUtc ?? DateTime.UtcNow,
                correlation,
                bus
            );
        await SubmitSignatureHandler.PublishCompletedAsync(request, correlation, bus);

        return Result.Success(Report(command, request, completed: true));
    }

    private static ReconcileCompletionReport Report(
        ReconcileCompletionCommand command,
        SignatureRequest request,
        bool completed
    ) => new(request.Id, command.DryRun, completed, request.Status.ToString());
}
