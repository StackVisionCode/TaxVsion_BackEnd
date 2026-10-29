using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;
using TaxVision.Documents.Domain.Branding;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Documents.Application.Generations.ClientFormSummary;

/// <summary>
/// Render + subida del Submission Summary: payload cifrado → descifrado SÓLO en memoria → HTML Fluid →
/// PDF Chromium → SaveFileRequested (carpeta ClientForms, owner del participante). No persiste el snapshot
/// en claro: lo lee del ProtectedPayload que llega en el comando. Molde: ProcessSaaSReceiptGeneration.
/// </summary>
public static class ProcessClientFormSummaryGenerationHandler
{
    private const string FolderTypeClientForms = "ClientForms";
    private const string PdfContentType = "application/pdf";

    public static async Task Handle(
        ProcessClientFormSummaryGenerationCommand command,
        IDocumentGenerationRepository repository,
        IProtectedPayloadCodec codec,
        IDocumentBrandingRepository brandingRepository,
        ITenantLogoResolver tenantLogoResolver,
        IDocumentTemplateRenderer renderer,
        IHtmlToPdfConverter pdfConverter,
        IDocumentStorageClient storageClient,
        IPdfPageCounter pageCounter,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IPlatformSettings platform,
        TimeProvider clock,
        ILogger<ProcessClientFormSummaryGenerationCommand> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? command.GenerationId.ToString("N")
            : command.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var generation = await repository.GetByIdAsync(command.TenantId, command.GenerationId, ct);
            if (generation is null)
            {
                logger.LogWarning("ProcessClientFormSummary: {GenerationId} not found.", command.GenerationId);
                return;
            }

            if (generation.Status is not (DocumentGenerationStatus.Requested or DocumentGenerationStatus.Queued))
                return; // Redelivery: ya se está generando o ya se generó.

            var payload = Unprotect(command, codec, logger);
            if (payload is null)
            {
                // El payload no valida contra el sobre: fallo terminal, no se reintenta en bucle.
                await FailAsync(
                    generation,
                    new Error("Documents.ClientFormSummary.InvalidPayload", "Protected payload validation failed."),
                    unitOfWork,
                    bus,
                    correlationId,
                    clock.GetUtcNow().UtcDateTime,
                    logger,
                    ct
                );
                return;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            generation.Queue(now);
            generation.StartRendering(now);

            var branding = await ResolveBrandingAsync(
                command.TenantId,
                brandingRepository,
                tenantLogoResolver,
                logger,
                ct
            );
            var fileName = BuildFileName(payload.Snapshot);

            var pdf = await RenderAsync(
                payload.Snapshot,
                branding,
                platform.Current.BrandName,
                command.SignatureCopy is not null,
                renderer,
                pdfConverter,
                ct
            );
            if (pdf.IsFailure)
            {
                await FailAsync(generation, pdf.Error, unitOfWork, bus, correlationId, now, logger, ct);
                return;
            }

            var hash = ContentHash.Create(
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(pdf.Value))
            );
            if (hash.IsSuccess)
                generation.SetContentHash(hash.Value, fileName);

            var fileId = Guid.NewGuid();
            var startUpload = generation.StartUploading(fileId, now);
            if (startUpload.IsFailure)
            {
                await FailAsync(generation, startUpload.Error, unitOfWork, bus, correlationId, now, logger, ct);
                return;
            }

            // Persistir el FileId ANTES de subir: el consumer de FileAvailable correlaciona por él.
            await unitOfWork.SaveChangesAsync(ct);

            await bus.PublishAsync(
                new DocumentGenerationStartedIntegrationEvent
                {
                    TenantId = generation.TenantId,
                    CorrelationId = correlationId,
                    GenerationId = generation.Id,
                    DocumentType = ClientFormSubmissionSummaryRequestedConsumer.DocumentType,
                }
            );

            var save = await storageClient.RequestSaveAsync(
                tenantId: command.TenantId,
                fileId: fileId,
                content: pdf.Value,
                fileName: fileName,
                contentType: PdfContentType,
                ownerType: generation.Owner.OwnerType,
                ownerId: generation.Owner.OwnerId,
                folderType: FolderTypeClientForms,
                taxYear: command.TaxYear,
                actorId: command.TenantId,
                correlationId: correlationId,
                ct: ct
            );

            if (save.IsFailure)
            {
                await FailAsync(generation, save.Error, unitOfWork, bus, correlationId, now, logger, ct);
                return;
            }

            // P-18: segunda copia del mismo PDF para Signature (owner/carpeta de firma), con su propio FileId.
            if (command.SignatureCopy is { } signatureCopy)
            {
                var signatureFileId = Guid.NewGuid();
                var copySave = await storageClient.RequestSaveAsync(
                    tenantId: command.TenantId,
                    fileId: signatureFileId,
                    content: pdf.Value,
                    fileName: fileName,
                    contentType: PdfContentType,
                    ownerType: signatureCopy.OwnerType,
                    ownerId: generation.Owner.OwnerId,
                    folderType: signatureCopy.FolderType,
                    taxYear: signatureCopy.TaxYear ?? command.TaxYear,
                    actorId: command.TenantId,
                    correlationId: correlationId,
                    ct: ct
                );

                if (copySave.IsSuccess)
                {
                    // Sólo se anuncia la copia si quedó subida: así Tasks nunca pide firmar un archivo inexistente.
                    var anchorPage = ResolveAnchorPage(pageCounter, pdf.Value, logger);
                    generation.AttachSignatureCopy(signatureFileId, anchorPage);
                    await unitOfWork.SaveChangesAsync(ct);
                }
                else
                {
                    logger.LogWarning(
                        "Client form summary {GenerationId}: signature copy could not be saved: {Code}.",
                        generation.Id,
                        copySave.Error.Code
                    );
                }
            }

            logger.LogInformation(
                "Client form summary {GenerationId} rendered ({Bytes} bytes) and handed to CloudStorage as file {FileId}.",
                generation.Id,
                pdf.Value.LongLength,
                fileId
            );
        }
    }

    private static SubmissionSummaryPayload? Unprotect(
        ProcessClientFormSummaryGenerationCommand command,
        IProtectedPayloadCodec codec,
        ILogger logger
    )
    {
        try
        {
            var binding = new ProtectedPayloadBinding(
                command.TenantId,
                command.FormRequestId,
                command.Participant,
                IdempotencyKey: command.IdempotencyKey
            );
            var payload = codec.Unprotect<SubmissionSummaryPayload>(
                command.Payload,
                ClientFormProtectedPayloadSchemas.SubmissionSummary,
                binding
            );
            if (payload.TenantId != command.TenantId || payload.FormRequestId != command.FormRequestId)
                throw new CryptographicException("Payload binding does not match the command.");
            return payload;
        }
        catch (CryptographicException)
        {
            logger.LogWarning(
                "Security: client form summary {GenerationId} payload failed validation at render.",
                command.GenerationId
            );
            return null;
        }
    }

    // Página (1-indexada) donde la plantilla reservó la caja de firma = última página del PDF.
    private static int ResolveAnchorPage(IPdfPageCounter pageCounter, byte[] pdf, ILogger logger)
    {
        try
        {
            var pages = pageCounter.CountPages(pdf);
            return pages >= 1 ? pages : 1;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Signature anchor: page count failed; defaulting to page 1.");
            return 1;
        }
    }

    private static async Task<Result<byte[]>> RenderAsync(
        SubmissionSummarySnapshot snapshot,
        BrandingPayload? branding,
        string platformBrand,
        bool includeSignatureBox,
        IDocumentTemplateRenderer renderer,
        IHtmlToPdfConverter pdfConverter,
        CancellationToken ct
    )
    {
        var data = BuildRenderData(snapshot, branding, platformBrand, includeSignatureBox);
        var html = await renderer.RenderHtmlAsync(
            ClientFormSubmissionSummaryRequestedConsumer.TemplateKey,
            1,
            Guid.Empty,
            data,
            ct
        );
        return html.IsFailure ? Result.Failure<byte[]>(html.Error) : await pdfConverter.ConvertAsync(html.Value, ct);
    }

    private static IReadOnlyDictionary<string, object?> BuildRenderData(
        SubmissionSummarySnapshot snapshot,
        BrandingPayload? branding,
        string platformBrand,
        bool includeSignatureBox
    )
    {
        var culture = CultureInfo.InvariantCulture;
        var logo =
            branding?.LogoDataUri is { Length: > 0 } l && l.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? l
                : string.Empty;
        var brandColor = IsHexColor(branding?.BrandColorHex) ? branding!.BrandColorHex! : "#0074d4";
        var displayName = string.IsNullOrWhiteSpace(branding?.DisplayName) ? platformBrand : branding!.DisplayName!;

        var pages = snapshot
            .Pages.Select(
                object (page) =>
                    new Dictionary<string, object>
                    {
                        ["title"] = page.Title ?? string.Empty,
                        ["sections"] = page
                            .Sections.Select(
                                object (section) =>
                                    new Dictionary<string, object>
                                    {
                                        ["title"] = section.Title ?? string.Empty,
                                        ["items"] = ItemsToList(section.Items),
                                        ["rows"] = section
                                            .Rows.Select(
                                                object (row) =>
                                                    new Dictionary<string, object>
                                                    {
                                                        ["label"] = row.Label,
                                                        ["items"] = ItemsToList(row.Items),
                                                    }
                                            )
                                            .ToList(),
                                    }
                            )
                            .ToList(),
                    }
            )
            .ToList();

        var attachments = snapshot
            .Attachments.Select(
                object (a) => new Dictionary<string, object> { ["label"] = a.Label, ["fileName"] = a.FileName }
            )
            .ToList();

        var generatedOn = (snapshot.CompletedAtUtc ?? DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm 'UTC'", culture);

        return new Dictionary<string, object?>
        {
            ["summary"] = new Dictionary<string, object>
            {
                ["title"] = snapshot.Title,
                ["customerName"] = snapshot.CustomerDisplayName ?? string.Empty,
                ["versionNumber"] = snapshot.VersionNumber,
                ["revisionNumber"] = snapshot.RevisionNumber,
                ["submittedAt"] = snapshot.SubmittedAtUtc?.ToString("yyyy-MM-dd HH:mm 'UTC'", culture) ?? string.Empty,
                ["completedAt"] = snapshot.CompletedAtUtc?.ToString("yyyy-MM-dd HH:mm 'UTC'", culture) ?? string.Empty,
                ["generatedOn"] = generatedOn,
                ["pages"] = pages,
                ["attachments"] = attachments,
                ["hasAttachments"] = attachments.Count > 0,
                ["logo"] = logo,
                ["brandColor"] = brandColor,
                ["displayName"] = displayName,
                ["showSignatureBox"] = includeSignatureBox,
            },
        };
    }

    private static List<object> ItemsToList(IReadOnlyList<SubmissionSummaryItem> items) =>
        items
            .Select(object (i) => new Dictionary<string, object> { ["label"] = i.Label, ["value"] = i.Value })
            .ToList();

    private static async Task<BrandingPayload?> ResolveBrandingAsync(
        Guid tenantId,
        IDocumentBrandingRepository brandingRepository,
        ITenantLogoResolver tenantLogoResolver,
        ILogger logger,
        CancellationToken ct
    )
    {
        var stored = await brandingRepository.GetByTenantAsync(tenantId, ct);
        BrandingPayload? branding = stored is null
            ? null
            : new BrandingPayload(stored.DisplayName, stored.LogoDataUri, stored.BrandColorHex, stored.FooterText);

        if (string.IsNullOrWhiteSpace(branding?.LogoDataUri))
        {
            string? tenantLogo = null;
            try
            {
                tenantLogo = await tenantLogoResolver.ResolveLogoDataUriAsync(tenantId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Client form summary: tenant logo resolution failed for {TenantId}.", tenantId);
            }
            // Sin logo de la oficina (o si falla su descarga) se usa el del sistema.
            if (string.IsNullOrEmpty(tenantLogo))
            {
                try
                {
                    tenantLogo = await tenantLogoResolver.ResolveLogoDataUriAsync(PlatformTenant.Id, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Client form summary: system logo resolution failed.");
                }
            }
            if (!string.IsNullOrEmpty(tenantLogo))
                branding = (branding ?? new BrandingPayload(null, null, null, null)) with { LogoDataUri = tenantLogo };
        }

        return branding;
    }

    // "{Title} - Submission summary (rev {N}).pdf", saneado para nombre de archivo.
    private static string BuildFileName(SubmissionSummarySnapshot snapshot)
    {
        var title = string.IsNullOrWhiteSpace(snapshot.Title) ? "Form" : snapshot.Title.Trim();
        var sb = new StringBuilder(title.Length);
        foreach (var c in title)
            sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        var safe = sb.ToString().Trim();
        if (safe.Length > 120)
            safe = safe[..120].Trim();
        return $"{safe} - Submission summary (rev {snapshot.RevisionNumber}).pdf";
    }

    private static async Task FailAsync(
        DocumentGeneration generation,
        Error error,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        string correlationId,
        DateTime now,
        ILogger logger,
        CancellationToken ct
    )
    {
        generation.Fail(error.Code, error.Message, now);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new DocumentGenerationFailedIntegrationEvent
            {
                TenantId = generation.TenantId,
                CorrelationId = correlationId,
                GenerationId = generation.Id,
                ExternalReferenceId = generation.ExternalReferenceId,
                DocumentType = generation.DocumentType.Value,
                OwnerType = generation.Owner.OwnerType,
                OwnerId = generation.Owner.OwnerId,
                ErrorCode = error.Code,
            }
        );

        logger.LogWarning(
            "Client form summary generation {GenerationId} failed: {ErrorCode} — {ErrorMessage}.",
            generation.Id,
            error.Code,
            error.Message
        );
    }

    private static bool IsHexColor(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is 4 or 7
        && value[0] == '#'
        && value.AsSpan(1).ToArray().All(Uri.IsHexDigit);
}
