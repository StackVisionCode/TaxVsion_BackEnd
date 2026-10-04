using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Public.GetDocument;

/// <summary>
/// F5 — Sirve el PDF original al firmante autenticado por token. Fases explícitas:
/// <list type="number">
///   <item>Resolver token (firma, epoch, denylist).</item>
///   <item>Rechazar si la verificación requerida no está completa.</item>
///   <item>Rechazar si el archivo no está disponible (scan).</item>
///   <item>Descargar bytes vía M2M (<see cref="ISignatureCloudStorageClient"/>).</item>
///   <item>Registrar <c>RecordSignerDocumentFirstView</c> en el aggregate (idempotente).</item>
///   <item>Publicar <see cref="SignerDocumentViewedIntegrationEvent"/> para appender al audit chain.</item>
/// </list>
/// </summary>
public static class GetPublicDocumentHandler
{
    public static async Task<Result<PublicDocumentStream>> Handle(
        GetPublicDocumentCommand cmd,
        ISigningTokenService tokenService,
        ISignatureRequestRepository repository,
        IFileMetadataRefRepository fileRepository,
        ISignatureCloudStorageClient cloudStorage,
        IJtiDenylist denylist,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var resolution = await PublicTokenResolver.ResolveAsync(cmd.Token, tokenService, repository, denylist, ct);
        if (resolution.IsFailure)
            return Result.Failure<PublicDocumentStream>(resolution.Error);

        var (request, signer) = (resolution.Value.Request, resolution.Value.Signer);

        var gate = EnsureVerificationCompleted(signer);
        if (gate.IsFailure)
            return Result.Failure<PublicDocumentStream>(gate.Error);

        var fileRef = await fileRepository.GetByFileIdAsync(request.TenantId, request.OriginalFileId, ct);
        if (fileRef is null || fileRef.Status != FileScanStatus.Available)
            return Result.Failure<PublicDocumentStream>(
                new Error(
                    "Signature.Document.NotAvailable",
                    "The document is not available yet. Try again in a moment."
                )
            );

        var download = await cloudStorage.DownloadAsync(request.TenantId, request.OriginalFileId, ct);
        if (download.IsFailure)
            return Result.Failure<PublicDocumentStream>(download.Error);

        var viewedAt = DateTime.UtcNow;
        var record = request.RecordSignerDocumentFirstView(signer.Id, viewedAt, cmd.ClientIp, cmd.UserAgent);
        if (record.IsFailure)
            return Result.Failure<PublicDocumentStream>(record.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new SignerDocumentViewedIntegrationEvent
            {
                TenantId = request.TenantId,
                CorrelationId = correlation.CorrelationId,
                SignatureRequestId = request.Id,
                CreatedByUserId = request.CreatedByUserId,
                SignerId = signer.Id,
                ViewedAtUtc = viewedAt,
                ClientIp = cmd.ClientIp,
            }
        );

        var contentType = string.IsNullOrWhiteSpace(fileRef.ContentType) ? "application/pdf" : fileRef.ContentType;
        var fileName = $"{SanitizeFileName(request.Title)}.pdf";
        return Result.Success(new PublicDocumentStream(download.Value, contentType, fileName));
    }

    // El audit del enlace (SignerViewed) ya se registra al abrir /{token}; aquí sólo exigimos que la
    // verificación requerida esté satisfecha. Antes era un gate de UI; ahora es del endpoint.
    private static Result EnsureVerificationCompleted(Signer signer)
    {
        if (signer.RequiredVerificationMethod is { } method && !signer.HasCompletedVerification(method))
            return Result.Failure(
                new Error(
                    "Signature.Document.VerificationRequired",
                    "Complete the identity verification before viewing the document."
                )
            );
        return Result.Success();
    }

    // Para el Content-Disposition: evita caracteres que confunden el navegador o rutas.
    private static string SanitizeFileName(string raw)
    {
        var cleaned = new string(raw.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "document" : cleaned;
    }
}
