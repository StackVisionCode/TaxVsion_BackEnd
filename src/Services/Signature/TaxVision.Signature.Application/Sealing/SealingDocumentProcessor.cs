using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>Fase A, documentos: descarga el original, aplica el sello y lo deja en MinIO. No toca el agregado.</summary>
public sealed class SealingDocumentProcessor(
    ISignatureCloudStorageClient storage,
    IDocumentSealingEngine sealer,
    IStampStyleResolver stampStyles,
    ILogger<SignatureRequest> logger
)
{
    public async Task<Result> SealAndStageAsync(
        SignatureRequest request,
        IReadOnlyList<Guid> requestedDocumentIds,
        DateTime evidenceAtUtc,
        IReadOnlySet<Guid> readyImageSignerIds,
        SealingRun run,
        CancellationToken ct
    )
    {
        // Bajamos aquí (donde vive el I/O de CloudStorage) el PNG de firma de cada firmante cuyo archivo
        // ya pasó el scan (readyImageSignerIds), para que el engine estampe la imagen y quede puro (sin I/O).
        var signatureImages = await DownloadSignatureImagesAsync(request, readyImageSignerIds, ct);
        // Marco, formato de fecha y letra automática del tenant: el mismo estilo que vio el preparador.
        var stampStyle = await stampStyles.ResolveAsync(request.TenantId, ct);

        // Firma reutilizable del preparador (canal paralelo, Form 8879). Best-effort: la imagen se subió al
        // crear el perfil (F1), así que ya está Available; si por lo que sea no baja, cae al sello tipográfico.
        var preparerImage = await DownloadPreparerSignatureAsync(request, ct);

        var requested = requestedDocumentIds.ToHashSet();
        foreach (var document in request.Documents.OrderBy(document => document.Order))
        {
            if (
                !requested.Contains(document.Id)
                || document.SealedFileId is not null
                || !request.IsDocumentReadyForSealing(document.Id)
            )
                continue;

            var originalBytesResult = await storage.DownloadAsync(request.TenantId, document.OriginalFileId, ct);
            if (originalBytesResult.IsFailure)
                return Result.Failure(originalBytesResult.Error);

            var sealResult = TryApplySeal(
                request,
                evidenceAtUtc,
                document,
                originalBytesResult.Value,
                signatureImages,
                preparerImage,
                stampStyle
            );
            if (sealResult.IsFailure)
                return Result.Failure(sealResult.Error);

            var hashResult = DocumentHash.Create(sealResult.Value.ChecksumSha256);
            if (hashResult.IsFailure)
                return Result.Failure(hashResult.Error);

            var staged = await storage.StageAsync(
                request.TenantId,
                SealedFileUpload.For(request, sealResult.Value.SealedPdfBytes, document.Title, "_Signed.pdf"),
                ct
            );
            if (staged.IsFailure)
                return Result.Failure(staged.Error);

            run.Add(
                new SealedDocumentResult(
                    document.Id,
                    staged.Value.FileId,
                    sealResult.Value.ChecksumSha256,
                    DateTime.UtcNow
                ),
                staged.Value
            );
        }

        return Result.Success();
    }

    private async Task<IReadOnlyDictionary<Guid, byte[]>> DownloadSignatureImagesAsync(
        SignatureRequest request,
        IReadOnlySet<Guid> readyImageSignerIds,
        CancellationToken ct
    )
    {
        var images = new Dictionary<Guid, byte[]>();
        foreach (var signer in request.Signers)
        {
            if (signer.SignatureImageFileId is not { } imageFileId || !readyImageSignerIds.Contains(signer.Id))
                continue;

            var downloadResult = await storage.DownloadAsync(request.TenantId, imageFileId, ct);
            if (downloadResult.IsSuccess)
            {
                images[signer.Id] = downloadResult.Value;
                continue;
            }

            // El gate ya confirmó Available, así que un fallo aquí es transitorio (blip de MinIO/red),
            // no "archivo inexistente". Reintentar el sellado es preferible a sellar sin la firma real.
            throw new SignatureImageNotReadyException(
                $"Signature image {imageFileId} for signer {signer.Id} is Available but could not be downloaded ({downloadResult.Error.Code})."
            );
        }

        return images;
    }

    private async Task<byte[]?> DownloadPreparerSignatureAsync(SignatureRequest request, CancellationToken ct)
    {
        if (request.PreparerSignatureFileId is not { } fileId || request.PreparerFields.Count == 0)
            return null;

        var download = await storage.DownloadAsync(request.TenantId, fileId, ct);
        return download.IsSuccess ? download.Value : null;
    }

    /// <summary>
    /// PdfSharp lanza <c>Exception</c> genérica ante un PDF ilegible: es permanente. La red (TSA) es
    /// transitoria.
    /// </summary>
    private Result<SealingResult> TryApplySeal(
        SignatureRequest request,
        DateTime evidenceAtUtc,
        RequestDocument document,
        byte[] originalBytes,
        IReadOnlyDictionary<Guid, byte[]> signatureImages,
        byte[]? preparerImage,
        StampStyle stampStyle
    )
    {
        try
        {
            return Result.Success(
                ApplySeal(request, evidenceAtUtc, document, originalBytes, signatureImages, preparerImage, stampStyle)
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Sealing of document {DocumentId} failed transiently.", document.Id);
            return Result.Failure<SealingResult>(new Error(SignatureStorage.TransientCode, ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sealing of document {DocumentId} failed permanently.", document.Id);
            return Result.Failure<SealingResult>(
                new Error("Signature.Sealing.InvalidPdf", "The document could not be sealed.")
            );
        }
    }

    private SealingResult ApplySeal(
        SignatureRequest request,
        DateTime evidenceAtUtc,
        RequestDocument document,
        byte[] originalBytes,
        IReadOnlyDictionary<Guid, byte[]> signatureImages,
        byte[]? preparerImage,
        StampStyle stampStyle
    )
    {
        var fields = BuildFieldRenders(request, document.Id, signatureImages, preparerImage);
        // Sin el id de la SignatureRequest: es un identificador interno sensible y no debe estamparse en
        // cada página del documento firmado. La integridad ya la ancla el "Doc SHA-256" del pie, y la
        // referencia del envelope vive en el Certificate of Completion (documento aparte).
        var footer = $"Completed {evidenceAtUtc:yyyy-MM-dd HH:mm} UTC";
        var sealingRequest = new SealingRequest(
            originalBytes,
            fields,
            document.DocumentHashPre?.Value ?? string.Empty,
            footer,
            stampStyle
        );
        return sealer.Seal(sealingRequest);
    }

    private static IReadOnlyList<SealedFieldRender> BuildFieldRenders(
        SignatureRequest request,
        Guid documentId,
        IReadOnlyDictionary<Guid, byte[]> signatureImages,
        byte[]? preparerImage
    )
    {
        var renders = new List<SealedFieldRender>();
        foreach (var signer in request.Signers)
        {
            var signedAt = signer.SignedAtUtc ?? DateTime.UtcNow;
            // Solo los campos de firma llevan la imagen; un campo de texto/fecha del mismo firmante
            // conserva su render tipográfico aunque exista PNG de firma.
            signatureImages.TryGetValue(signer.Id, out var signerImage);
            foreach (var field in signer.Fields.Where(field => field.DocumentId == documentId))
            {
                var textValue =
                    field.Kind == SignatureFieldKind.Text
                        ? signer.FieldValues.FirstOrDefault(v => v.FieldId == field.Id)?.Value
                        : null;
                renders.Add(
                    new SealedFieldRender(
                        Page: field.Position.Page,
                        X: field.Position.X,
                        Y: field.Position.Y,
                        Width: field.Position.Width,
                        Height: field.Position.Height,
                        Kind: field.Kind,
                        Label: field.Label,
                        SignerDisplayName: signer.FullName.Value,
                        SignedAtUtc: signedAt,
                        SignatureImageBytes: field.Kind == SignatureFieldKind.Signature ? signerImage : null,
                        Value: textValue,
                        FontSizePt: field.FontSizePt
                    )
                );
            }
        }

        // Campos del preparador (canal paralelo): se estampan con su firma reutilizable al sellar, nunca
        // antes → el firmante no la ve. Sin imagen (perfil sin subir/baja fallida) → sello tipográfico.
        var preparerName = request.Preparer?.DisplayName ?? "Preparer";
        var preparerSignedAt = request.PreparerSignedAtUtc ?? request.CompletedAtUtc ?? DateTime.UtcNow;
        foreach (var field in request.PreparerFields.Where(field => field.DocumentId == documentId))
        {
            renders.Add(
                new SealedFieldRender(
                    Page: field.Position.Page,
                    X: field.Position.X,
                    Y: field.Position.Y,
                    Width: field.Position.Width,
                    Height: field.Position.Height,
                    Kind: field.Kind,
                    Label: field.Label,
                    SignerDisplayName: preparerName,
                    SignedAtUtc: preparerSignedAt,
                    SignatureImageBytes: field.Kind == SignatureFieldKind.Signature ? preparerImage : null,
                    Value: null
                )
            );
        }
        return renders;
    }
}
