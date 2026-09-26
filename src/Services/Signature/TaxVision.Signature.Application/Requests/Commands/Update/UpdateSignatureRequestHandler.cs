using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Categories;

namespace TaxVision.Signature.Application.Requests.Commands.Update;

/// <summary>
/// Edita un borrador cableando los setters de dominio (todos exigen Draft/Ready). Si alguno falla
/// (p. ej. certificado activado sin generarlo) se devuelve ese error y no se guarda nada.
/// </summary>
public static class UpdateSignatureRequestHandler
{
    public static async Task<Result> Handle(
        UpdateSignatureRequestCommand cmd,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        ISignatureRequestListCacheInvalidator listCache,
        ISignatureCategoryResolver categoryResolver,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return NotFound();

        var category = await categoryResolver.ResolveAsync(cmd.TenantId, cmd.Category, ct);
        if (category.IsFailure)
            return Result.Failure(category.Error);

        var metadata = request.UpdateMetadata(cmd.Title, cmd.Description, category.Value, cmd.TokenExpirationHours);
        if (metadata.IsFailure)
            return metadata;

        // Flags de entrega/reminders: solo se tocan si vienen en el body (edición parcial).
        if (cmd.SendSignedDocumentToSigners is { } signedDelivery)
        {
            var applied = request.SetSignedDocumentDelivery(signedDelivery);
            if (applied.IsFailure)
                return applied;
        }

        if (cmd.SendCertificateToSigners is { } certificateDelivery)
        {
            var applied = request.SetCertificateDelivery(certificateDelivery);
            if (applied.IsFailure)
                return applied;
        }

        if (cmd.AutoRemindersEnabled is { } remindersEnabled)
        {
            var applied = request.SetReminderPolicy(
                remindersEnabled,
                cmd.ReminderIntervalHours ?? request.ReminderIntervalHours
            );
            if (applied.IsFailure)
                return applied;
        }

        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);
        return Result.Success();
    }

    private static Result NotFound() =>
        Result.Failure(
            new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
        );
}
