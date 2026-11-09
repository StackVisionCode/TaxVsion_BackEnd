using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Branding;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;

/// <summary>
/// Ejecución asíncrona de la generación (corre en un scope de Wolverine SIN tenant ambiental — todo
/// se resuelve por el tenantId explícito del comando). Pipeline: datos → HTML (Fluid) → PDF (Chromium)
/// → PUT al bucket temporal + SaveFileRequested (CloudStorage almacena). No termina la generación:
/// eso lo hace <see cref="DocumentFileAvailableConsumer"/> cuando CloudStorage confirma con FileAvailable.
///
/// Los fallos son terminales y observables: se marca la generación como Failed y se publica
/// DocumentGenerationFailed — NUNCA se lanza excepción (lanzar dejaría la fila atascada en Rendering al
/// hacer rollback). La clasificación transitorio/reintentable es de una fase posterior.
/// </summary>
public static class ProcessInvoiceGenerationHandler
{
    private const string DocumentTypeInvoice = "Invoice";
    private const string FolderTypeInvoices = "Invoices";
    private const string PdfContentType = "application/pdf";

    public static async Task Handle(
        ProcessInvoiceGenerationCommand command,
        IDocumentGenerationRepository repository,
        IDocumentBrandingRepository brandingRepository,
        ITenantLogoResolver tenantLogoResolver,
        IDocumentTemplateRenderer renderer,
        IHtmlToPdfConverter pdfConverter,
        IDocumentStorageClient storageClient,
        IQrCodeGenerator qrGenerator,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        IPlatformSettings platform,
        ILogger<ProcessInvoiceGenerationCommand> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(command)))
        {
            var generation = await repository.GetByIdAsync(command.TenantId, command.GenerationId, ct);
            if (generation is null)
            {
                logger.LogWarning(
                    "ProcessInvoiceGeneration: generation {GenerationId} not found for tenant {TenantId}; ignoring.",
                    command.GenerationId,
                    command.TenantId
                );
                return;
            }

            // Redelivery / ya avanzada: solo procesamos desde el arranque. Si ya subió/almacenó, no-op.
            if (generation.Status is not (DocumentGenerationStatus.Requested or DocumentGenerationStatus.Queued))
            {
                logger.LogInformation(
                    "ProcessInvoiceGeneration: generation {GenerationId} already in {Status}; skipping.",
                    generation.Id,
                    generation.Status
                );
                return;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            generation.Queue(now);
            generation.StartRendering(now);

            // Branding efectivo: el perfil guardado del tenant, y lo que venga en el request lo puede
            // sobrescribir campo a campo (override puntual sin tocar el perfil).
            var storedBranding = await brandingRepository.GetByTenantAsync(command.TenantId, ct);
            var effectiveBranding = InvoiceRenderModelBuilder.MergeBranding(command.Branding, storedBranding);

            // Si no hay un logo embebido explícito (request u override guardado en Documents), usar el logo
            // de la marca del tenant (Company settings → TenantBrands), bajado on-demand de CloudStorage.
            // Best-effort ESTRICTO: el logo nunca debe tumbar la generación de la factura, así que si el
            // resolver LANZA (proyección de logo ausente, storage caído) se sigue sin logo — no solo el
            // caso null/empty. (Hueco encontrado en un E2E: el mismo resolver, sin proteger, tumbaba la
            // generación del recibo de onboarding con Invalid object name 'TenantLogoRefs'.)
            // La factura la recibe el cliente: logo del portal, con el del CRM de respaldo.
            if (string.IsNullOrWhiteSpace(effectiveBranding?.LogoDataUri))
            {
                string? tenantLogo = null;
                try
                {
                    tenantLogo = await tenantLogoResolver.ResolveLogoDataUriAsync(
                        command.TenantId,
                        LogoAudience.Client,
                        ct
                    );
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Invoice generation {GenerationId}: tenant logo resolution failed; rendering without the tenant logo.",
                        command.GenerationId
                    );
                }

                if (!string.IsNullOrEmpty(tenantLogo))
                    effectiveBranding = (effectiveBranding ?? new BrandingPayload(null, null, null, null)) with
                    {
                        LogoDataUri = tenantLogo,
                    };
            }

            var pdf = await RenderAndConvertAsync(
                command,
                effectiveBranding,
                platform.Current.BrandName,
                renderer,
                pdfConverter,
                qrGenerator,
                ct
            );
            if (pdf.IsFailure)
            {
                await FailAsync(generation, command, pdf.Error, unitOfWork, bus, now, logger, ct);
                return;
            }

            // Hash del contenido (dedup/verificación) + nombre final, persistidos con la generación.
            var hash = ContentHash.Create(Convert.ToHexStringLower(SHA256.HashData(pdf.Value)));
            if (hash.IsSuccess)
                generation.SetContentHash(hash.Value, command.FileName);

            var fileId = Guid.NewGuid();
            var startUpload = generation.StartUploading(fileId, now);
            if (startUpload.IsFailure)
            {
                await FailAsync(generation, command, startUpload.Error, unitOfWork, bus, now, logger, ct);
                return;
            }

            // Persistir Uploading + FileId ANTES de que CloudStorage pueda responder FileAvailable:
            // el consumer correlaciona por ese FileId, así que la fila debe existir cuando llegue.
            await unitOfWork.SaveChangesAsync(ct);

            await bus.PublishAsync(
                new DocumentGenerationStartedIntegrationEvent
                {
                    TenantId = generation.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    GenerationId = generation.Id,
                    DocumentType = DocumentTypeInvoice,
                }
            );

            var upload = await storageClient.UploadAsync(fileId, pdf.Value, command.FileName, PdfContentType, ct);
            if (upload.IsFailure)
            {
                await FailAsync(generation, command, upload.Error, unitOfWork, bus, now, logger, ct);
                return;
            }

            await bus.RequestSaveAsync(
                upload.Value,
                new GeneratedFileDestination(
                    generation.TenantId,
                    command.FileName,
                    command.OwnerType,
                    command.OwnerId,
                    FolderTypeInvoices,
                    command.TaxYear,
                    generation.TenantId,
                    correlation.CorrelationId
                )
            );

            logger.LogInformation(
                "Invoice generation {GenerationId} rendered ({Bytes} bytes) and handed to CloudStorage as file {FileId}.",
                generation.Id,
                pdf.Value.LongLength,
                fileId
            );
        }
    }

    private static async Task<Result<byte[]>> RenderAndConvertAsync(
        ProcessInvoiceGenerationCommand command,
        BrandingPayload? branding,
        string platformBrand,
        IDocumentTemplateRenderer renderer,
        IHtmlToPdfConverter pdfConverter,
        IQrCodeGenerator qrGenerator,
        CancellationToken ct
    )
    {
        var data = InvoiceRenderModelBuilder.Build(command, branding, platformBrand, qrGenerator);

        var html = await renderer.RenderHtmlAsync(
            command.TemplateKey,
            command.TemplateVersion,
            command.TenantId,
            data,
            ct
        );
        if (html.IsFailure)
            return Result.Failure<byte[]>(html.Error);

        return await pdfConverter.ConvertAsync(html.Value, ct);
    }

    private static async Task FailAsync(
        DocumentGeneration generation,
        ProcessInvoiceGenerationCommand command,
        Error error,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
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
                CorrelationId = command.CorrelationId,
                GenerationId = generation.Id,
                OwnerType = command.OwnerType,
                OwnerId = command.OwnerId,
                ErrorCode = error.Code,
            }
        );

        logger.LogWarning(
            "Invoice generation {GenerationId} failed: {ErrorCode} — {ErrorMessage}.",
            generation.Id,
            error.Code,
            error.Message
        );
    }

    private static string ResolveCorrelationId(ProcessInvoiceGenerationCommand command) =>
        string.IsNullOrWhiteSpace(command.CorrelationId) ? command.GenerationId.ToString("N") : command.CorrelationId;
}
