using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Requests.Preparer;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>Fase A, certificado: arma el Certificate of Completion con lo sellado y lo deja en MinIO.</summary>
public sealed class SealingCertificateBuilder(
    ICertificateOfCompletionRenderer renderer,
    IDocumentSealingEngine sealer,
    ISignatureCloudStorageClient storage,
    ITenantBrandingRefRepository brandingRepository,
    ILogger<SignatureRequest> logger
)
{
    private sealed record CertificateRenderArtifact(Guid? DocumentId, string Title, byte[] Bytes);

    public async Task<Result> GenerateAndStageAsync(SignatureRequest request, SealingRun run, CancellationToken ct)
    {
        var certificateArtifactsResult = await GenerateCertificateArtifactsAsync(request, run, ct);
        if (certificateArtifactsResult.IsFailure)
            return Result.Failure(certificateArtifactsResult.Error);

        foreach (var artifact in certificateArtifactsResult.Value)
        {
            var certificateUpload = SealedFileUpload.For(request, artifact.Bytes, artifact.Title, "_Certificate.pdf");
            var staged = await storage.StageAsync(request.TenantId, certificateUpload, ct);
            if (staged.IsFailure)
            {
                logger.LogWarning(
                    "Certificate upload failed for {RequestId}: {Error}",
                    request.Id,
                    staged.Error.Message
                );
                return Result.Failure(staged.Error);
            }

            run.Add(new SealedCertificateResult(artifact.DocumentId, staged.Value.FileId), staged.Value);
        }

        return Result.Success();
    }

    /// <summary>
    /// Genera SOLO los bytes del Certificate of Completion (incluye la descarga del logo de oficina/sistema).
    /// Devuelve una lista vacía si la request no pidió certificado.
    /// </summary>
    private async Task<Result<IReadOnlyList<CertificateRenderArtifact>>> GenerateCertificateArtifactsAsync(
        SignatureRequest request,
        SealingRun run,
        CancellationToken ct
    )
    {
        if (!request.GenerateCertificate)
            return Result.Success<IReadOnlyList<CertificateRenderArtifact>>([]);

        // Cert con que se sellan los documentos (null si el sellado está degradado) → panel "Digital seal".
        var seal = sealer.DescribeSealCertificate();

        // Logo de la OFICINA: el certificado lo recibe el cliente, así que va el del portal y, si no hay,
        // el del CRM. Al lado, el logo del SISTEMA: la marca del tenant plataforma (jturbi), misma fuente.
        // Si la oficina no tiene logo queda solo el del sistema; si el sistema tampoco, el renderer cae al
        // logo de plataforma embebido.
        var (issuerName, officeLogo) = await ResolveBrandingAsync(
            request.TenantId,
            TenantLogoSurfaces.ClientPreference,
            ct
        );

        byte[]? platformLogo = null;
        if (request.TenantId != PlatformTenant.Id)
        {
            var (_, resolvedPlatformLogo) = await ResolveBrandingAsync(
                PlatformTenant.Id,
                TenantLogoSurfaces.StaffPreference,
                ct
            );
            platformLogo = resolvedPlatformLogo;
        }

        if (request.CertificateGenerationMode == CertificateGenerationMode.SingleForRequest)
        {
            if (request.CertificateFileId is not null)
                return Result.Success<IReadOnlyList<CertificateRenderArtifact>>([]);

            var model = BuildCertificateModel(
                request,
                run,
                issuerName,
                platformLogo,
                officeLogo,
                seal,
                documentId: null
            );
            var rendered = renderer.Render(model);
            return Result.Success<IReadOnlyList<CertificateRenderArtifact>>([
                new CertificateRenderArtifact(null, request.Title, rendered.CertificatePdfBytes),
            ]);
        }

        var artifacts = request
            .Documents.Where(document => document.CertificateFileId is null)
            .OrderBy(document => document.Order)
            .Select(document =>
            {
                var model = BuildCertificateModel(
                    request,
                    run,
                    issuerName,
                    platformLogo,
                    officeLogo,
                    seal,
                    document.Id
                );
                return new CertificateRenderArtifact(
                    document.Id,
                    document.Title,
                    renderer.Render(model).CertificatePdfBytes
                );
            })
            .ToList();
        return Result.Success<IReadOnlyList<CertificateRenderArtifact>>(artifacts);
    }

    private async Task<(string? IssuerName, byte[]? TenantLogo)> ResolveBrandingAsync(
        Guid tenantId,
        IReadOnlyList<TenantLogoSurface> logoPreference,
        CancellationToken ct
    )
    {
        var branding = await brandingRepository.GetByTenantIdAsync(tenantId, ct);
        var issuerName = branding?.IssuerDisplayName;

        // Best-effort: un logo que no baja no frena el certificado; se prueba la siguiente superficie.
        var logos = await brandingRepository.ListLogosAsync(tenantId, ct);
        foreach (var surface in logoPreference)
        {
            var logo = logos.FirstOrDefault(l => l.Surface == surface && l.HasLogo);
            if (logo is null)
                continue;

            var logoResult = await storage.DownloadAsync(tenantId, logo.LogoFileId!.Value, ct);
            if (logoResult.IsSuccess)
                return (issuerName, logoResult.Value);

            logger.LogWarning(
                "Tenant {Surface} logo download failed for the certificate (tenant {TenantId}).",
                surface,
                tenantId
            );
        }

        return (issuerName, null);
    }

    private static CertificateOfCompletionModel BuildCertificateModel(
        SignatureRequest request,
        SealingRun run,
        string? issuerName,
        byte[]? platformLogo,
        byte[]? tenantLogo,
        CertificateSealInfo? seal,
        Guid? documentId
    )
    {
        var documents = request
            .Documents.Where(document => documentId is null || document.Id == documentId.Value)
            .OrderBy(document => document.Order)
            .ToList();
        var participatingSignerIds = documents
            .SelectMany(document =>
                request.Signers.Where(signer => signer.Fields.Any(field => field.DocumentId == document.Id))
            )
            .Select(signer => signer.Id)
            .ToHashSet();

        return new(
            SignatureRequestId: request.Id,
            Title: documentId is null ? request.Title : documents[0].Title,
            Category: request.Category,
            CreatedAtUtc: request.CreatedAtUtc,
            CompletedAtUtc: request.CompletedAtUtc ?? DateTime.UtcNow,
            SignersGlobal: request
                .Signers.Where(s => documentId is null || participatingSignerIds.Contains(s.Id))
                .Select(s => new CertificateSignerEntry(
                    s.Id,
                    s.FullName.Value,
                    s.Email.Value,
                    s.Order,
                    s.Status,
                    s.FirstViewedAtUtc,
                    s.ConsentAcceptedAtUtc,
                    s.SignedAtUtc,
                    s.ClientIp,
                    s.UserAgent,
                    FormatVerification(s.RequiredVerificationMethod),
                    FormatCapture(s.CaptureMethod)
                ))
                .ToList(),
            Documents: documents
                .Select(document => new CertificateDocumentEntry(
                    document.Id,
                    document.Order,
                    document.Title,
                    document.DocumentHashPre?.Value ?? string.Empty,
                    run.Find(document.Id)?.HashPost ?? document.DocumentHashPost?.Value ?? string.Empty,
                    run.Find(document.Id)?.SealedAtUtc
                        ?? document.SealedAtUtc
                        ?? request.CompletedAtUtc
                        ?? DateTime.UtcNow,
                    request
                        .Signers.Where(signer => signer.Fields.Any(field => field.DocumentId == document.Id))
                        .Select(signer => new CertificateDocumentSignerEntry(
                            signer.Id,
                            signer.FullName.Value,
                            signer
                                .DocumentCompletions.FirstOrDefault(completion => completion.DocumentId == document.Id)
                                ?.CompletedAtUtc
                                ?? signer.SignedAtUtc
                                ?? request.CompletedAtUtc
                                ?? DateTime.UtcNow,
                            signer.ClientIp,
                            signer.UserAgent
                        ))
                        .ToList()
                ))
                .ToList(),
            IssuerName: issuerName,
            PlatformLogo: platformLogo,
            TenantLogo: tenantLogo,
            // Referencia del preparador (ERO): solo si firmó internamente. Identificador enmascarado, sin imagen.
            Preparer: BuildPreparerEntry(request),
            Seal: seal
        );
    }

    /// <summary>Método de verificación de identidad del firmante, en texto corto para el acta (null = ninguno).</summary>
    private static string? FormatVerification(SignerVerificationMethod? method) =>
        method switch
        {
            SignerVerificationMethod.EmailOtp => "Email OTP",
            SignerVerificationMethod.SmsOtp => "SMS OTP",
            SignerVerificationMethod.WhatsAppOtp => "WhatsApp OTP",
            SignerVerificationMethod.PractitionerPin => "Practitioner PIN",
            SignerVerificationMethod.KbaQuiz => "Knowledge-based (KBA)",
            _ => null,
        };

    /// <summary>Cómo se capturó la firma, en texto corto para el acta (null = desconocido).</summary>
    private static string? FormatCapture(SignatureCaptureMethod? method) =>
        method switch
        {
            SignatureCaptureMethod.Typed => "Typed signature",
            SignatureCaptureMethod.Drawn => "Drawn signature",
            SignatureCaptureMethod.Uploaded => "Uploaded image",
            _ => null,
        };

    private static CertificatePreparerEntry? BuildPreparerEntry(SignatureRequest request)
    {
        // Un acta legal referencia partes por IDENTIDAD REAL (así lo hacen DocuSign/Adobe): solo se
        // incluye al preparador si su identidad 8879 (PreparerInfo: nombre + PTIN) está fijada. Una firma
        // "My Signature" estampada sin identidad NO genera una entrada con nombre inventado — la firma
        // visual vive en el documento; el acta lista partes identificadas.
        if (request.Preparer is not { } preparer)
            return null;

        var stampApplied = request.PreparerSignatureFileId is not null && request.PreparerFields.Count > 0;
        if (!request.IsPreparerSigned && !stampApplied)
            return null;

        var at = request.PreparerSignedAtUtc ?? request.CompletedAtUtc;
        return new CertificatePreparerEntry(preparer.DisplayName, MaskIdentifier(preparer.PtinOrEfin), at);
    }

    private static string MaskIdentifier(string identifier) => PreparerIdentifierMask.Mask(identifier);
}
