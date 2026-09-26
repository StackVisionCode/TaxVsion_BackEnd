using System.Globalization;
using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Application.Generations.Receipts;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Documents.Application.Generations.SaaSReceipt;

/// <summary>
/// Genera el PDF del recibo de una compra SaaS: mismo pipeline que el recibo de onboarding (datos → HTML
/// Fluid → PDF Chromium → bucket temporal + SaveFileRequested), pero **bajo el tenant real**. El emisor
/// sigue siendo la plataforma: es ella quien cobra. Los fallos son terminales y observables — nunca se lanza,
/// se marca Failed y se publica el evento.
/// </summary>
public static class ProcessSaaSReceiptGenerationHandler
{
    // Carpeta propia y no navegable: el recibo de la suscripción no es del gestor documental.
    private const string FolderTypeSaaSReceipts = "SaaSReceipts";
    private const string PdfContentType = "application/pdf";

    public static async Task Handle(
        ProcessSaaSReceiptGenerationCommand command,
        IDocumentGenerationRepository repository,
        IDocumentTemplateRenderer renderer,
        IHtmlToPdfConverter pdfConverter,
        IDocumentStorageClient storageClient,
        IPlatformIssuerProvider issuerProvider,
        ITenantLogoResolver tenantLogoResolver,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<ProcessSaaSReceiptGenerationCommand> logger,
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
                logger.LogWarning("ProcessSaaSReceiptGeneration: {GenerationId} not found.", command.GenerationId);
                return;
            }

            if (generation.Status is not (DocumentGenerationStatus.Requested or DocumentGenerationStatus.Queued))
                return; // Redelivery: ya se está generando o ya se generó.

            var now = clock.GetUtcNow().UtcDateTime;
            generation.Queue(now);
            generation.StartRendering(now);

            // El logo es best-effort ESTRICTO: el pago ya está confirmado, así que nada relacionado con la
            // marca puede tumbar el recibo.
            string? brandLogo = null;
            try
            {
                brandLogo = await tenantLogoResolver.ResolveLogoDataUriAsync(PlatformTenant.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SaaS receipt {GenerationId}: brand logo resolution failed.", generation.Id);
            }

            var pdf = await RenderAsync(command, issuerProvider.GetSnapshot(), brandLogo, renderer, pdfConverter, ct);
            if (pdf.IsFailure)
            {
                await FailAsync(generation, command, pdf.Error, unitOfWork, bus, now, logger, ct);
                return;
            }

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

            // Persistir el FileId ANTES de subir: el consumer de FileAvailable correlaciona por él.
            await unitOfWork.SaveChangesAsync(ct);

            await bus.PublishAsync(
                new DocumentGenerationStartedIntegrationEvent
                {
                    TenantId = generation.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    GenerationId = generation.Id,
                    DocumentType = GenerateSaaSReceiptDocumentHandler.DocumentTypeSaaSReceipt,
                }
            );

            var save = await storageClient.RequestSaveAsync(
                tenantId: command.TenantId,
                fileId: fileId,
                content: pdf.Value,
                fileName: command.FileName,
                contentType: PdfContentType,
                ownerType: GenerateSaaSReceiptDocumentHandler.OwnerTypeSaaSPayment,
                ownerId: command.SaaSPaymentId,
                folderType: FolderTypeSaaSReceipts,
                taxYear: command.Receipt.PaidAtUtc.Year,
                actorId: command.TenantId,
                correlationId: correlation.CorrelationId,
                ct: ct
            );

            if (save.IsFailure)
            {
                await FailAsync(generation, command, save.Error, unitOfWork, bus, now, logger, ct);
                return;
            }

            logger.LogInformation(
                "SaaS receipt {GenerationId} rendered ({Bytes} bytes) and handed to CloudStorage as file {FileId}.",
                generation.Id,
                pdf.Value.LongLength,
                fileId
            );
        }
    }

    private static async Task<Result<byte[]>> RenderAsync(
        ProcessSaaSReceiptGenerationCommand command,
        IssuerSnapshot issuer,
        string? brandLogoDataUri,
        IDocumentTemplateRenderer renderer,
        IHtmlToPdfConverter pdfConverter,
        CancellationToken ct
    )
    {
        var receipt = command.Receipt;
        var data = new Dictionary<string, object?>
        {
            ["receipt"] = new Dictionary<string, object>
            {
                ["officeName"] = receipt.OfficeName,
                ["description"] = receipt.Description,
                ["price"] = ReceiptRenderData.Money(receipt.AmountPaidCents),
                ["currency"] = receipt.Currency,
                ["paidAt"] = ReceiptRenderData.PaidAt(receipt.PaidAtUtc),
                ["transactionReferenceMask"] = receipt.TransactionReferenceMask,
                // Vacíos cuando el cobro no tiene unidades que contar; la plantilla omite la línea de desglose.
                ["quantity"] = receipt.Quantity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["unitPrice"] = receipt.UnitAmountCents is { } unit ? ReceiptRenderData.Money(unit) : string.Empty,
                ["issuer"] = ReceiptRenderData.Issuer(issuer, brandLogoDataUri),
            },
        };

        var html = await renderer.RenderHtmlAsync(
            command.TemplateKey,
            command.TemplateVersion,
            command.TenantId,
            data,
            ct
        );

        return html.IsFailure ? Result.Failure<byte[]>(html.Error) : await pdfConverter.ConvertAsync(html.Value, ct);
    }

    private static async Task FailAsync(
        DocumentGeneration generation,
        ProcessSaaSReceiptGenerationCommand command,
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
                OwnerType = GenerateSaaSReceiptDocumentHandler.OwnerTypeSaaSPayment,
                OwnerId = command.SaaSPaymentId,
                ErrorCode = error.Code,
            }
        );

        logger.LogWarning(
            "SaaS receipt generation {GenerationId} failed: {ErrorCode} — {ErrorMessage}.",
            generation.Id,
            error.Code,
            error.Message
        );
    }
}
