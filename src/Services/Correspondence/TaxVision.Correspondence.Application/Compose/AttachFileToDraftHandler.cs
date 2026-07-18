using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Fase 12 — HTTP-triggered, no un consumer Wolverine, mismo criterio que el resto de los
/// handlers de este servicio (no empuja correlación).
///
/// <para>
/// La verificación contra CloudStorage (<see cref="ICloudStorageClient.GetFileMetadataAsync"/>)
/// es deliberadamente best-effort: un fallo ahí (404, timeout, permiso M2M no otorgado en este
/// ambiente) solo se loguea como warning y el attach sigue adelante confiando en lo que mandó el
/// frontend. Esto no es un agujero de seguridad — CloudStorage ya validó los bytes reales cuando
/// el frontend subió el archivo por su propio flujo de upload; acá lo único que se persiste es una
/// referencia (<see cref="DraftAttachmentRef"/>), nunca bytes, así que un eco no verificado de
/// fileId/filename/contentType/sizeBytes es un límite de confianza de severidad baja, no un hueco
/// de seguridad. Bloquear el attach por un ping lateral que falló haría a Correspondence
/// innecesariamente frágil contra un chequeo que no es su fuente de verdad.
/// </para>
/// </summary>
public static class AttachFileToDraftHandler
{
    public static async Task<Result> Handle(
        AttachFileToDraftCommand command,
        IDraftRepository drafts,
        ICloudStorageClient cloudStorage,
        IUnitOfWork unitOfWork,
        ILogger<AttachFileToDraftCommand> logger,
        CancellationToken ct
    )
    {
        var draft = await drafts.GetByIdAsync(command.TenantId, command.DraftId, ct);
        if (draft is null)
            return Result.Failure(new Error("Draft.NotFound", "The draft was not found for this tenant."));

        await BestEffortVerifyFileAsync(command, cloudStorage, logger, ct);

        var attachmentRefResult = DraftAttachmentRef.Create(
            command.FileId,
            command.Filename,
            command.ContentType,
            command.SizeBytes
        );
        if (attachmentRefResult.IsFailure)
            return Result.Failure(attachmentRefResult.Error);

        var attachResult = draft.AttachFile(attachmentRefResult.Value);
        if (attachResult.IsFailure)
            return attachResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static async Task BestEffortVerifyFileAsync(
        AttachFileToDraftCommand command,
        ICloudStorageClient cloudStorage,
        ILogger<AttachFileToDraftCommand> logger,
        CancellationToken ct
    )
    {
        var metadata = await cloudStorage.GetFileMetadataAsync(command.TenantId, command.FileId, ct);
        if (metadata.IsFailure)
        {
            logger.LogWarning(
                "Could not verify file {FileId} against CloudStorage before attaching to draft {DraftId} ({ErrorCode}) — proceeding anyway.",
                command.FileId,
                command.DraftId,
                metadata.Error.Code
            );
            return;
        }

        if (metadata.Value.SizeBytes != command.SizeBytes || metadata.Value.ContentType != command.ContentType)
        {
            logger.LogWarning(
                "File {FileId} metadata mismatch when attaching to draft {DraftId}: caller supplied {CallerContentType}/{CallerSizeBytes}, CloudStorage reports {ActualContentType}/{ActualSizeBytes} — proceeding with caller-supplied values.",
                command.FileId,
                command.DraftId,
                command.ContentType,
                command.SizeBytes,
                metadata.Value.ContentType,
                metadata.Value.SizeBytes
            );
        }
    }
}
