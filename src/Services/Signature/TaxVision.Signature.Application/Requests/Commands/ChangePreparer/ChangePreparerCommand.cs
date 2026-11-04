using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Preparer;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.ChangePreparer;

/// <summary>Un admin (signature.request.manage) pasa el preparador de una solicitud enviada a otro empleado.</summary>
public sealed record ChangePreparerCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid NewPreparerUserId,
    Guid ActorUserId
);

public static class ChangePreparerHandler
{
    public static async Task<Result> Handle(
        ChangePreparerCommand cmd,
        ISignatureRequestRepository repository,
        PreparerReassigner reassigner,
        IUnitOfWork unitOfWork,
        ISignatureRequestListCacheInvalidator listCache,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure(
                new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
            );

        var reassigned = await reassigner.ReassignAsync(
            request,
            cmd.NewPreparerUserId,
            cmd.ActorUserId,
            SignaturePreparerChangeReasons.Manual,
            ct
        );
        if (reassigned.IsFailure)
            return reassigned;
        if (!reassigned.Value.Changed)
            return Result.Success();

        // Guardar antes de publicar: un reintento en línea no debe dejar escapar el aviso sin el cambio.
        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);
        await bus.PublishAsync(
            new SignaturePreparerChangedIntegrationEvent
            {
                TenantId = request.TenantId,
                CorrelationId = correlation.CorrelationId,
                SignatureRequestId = request.Id,
                Title = request.Title,
                CreatedByUserId = request.CreatedByUserId,
                PreviousPreparerUserId = reassigned.Value.PreviousPreparerUserId,
                NewPreparerUserId = cmd.NewPreparerUserId,
                NewPreparerDisplayName = reassigned.Value.NewPreparer.DisplayName,
                ChangedByUserId = cmd.ActorUserId,
                Reason = SignaturePreparerChangeReasons.Manual,
                ChangedAtUtc = request.UpdatedAtUtc,
            }
        );
        return Result.Success();
    }
}
