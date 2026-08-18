using System.Globalization;
using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
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
        IDocumentTemplateRenderer renderer,
        IHtmlToPdfConverter pdfConverter,
        IDocumentStorageClient storageClient,
        IQrCodeGenerator qrGenerator,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
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
            var effectiveBranding = ResolveBranding(command.Branding, storedBranding);

            var pdf = await RenderAndConvertAsync(command, effectiveBranding, renderer, pdfConverter, qrGenerator, ct);
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

            var save = await storageClient.RequestSaveAsync(
                tenantId: generation.TenantId,
                fileId: fileId,
                content: pdf.Value,
                fileName: command.FileName,
                contentType: PdfContentType,
                ownerType: command.OwnerType,
                ownerId: command.OwnerId,
                folderType: FolderTypeInvoices,
                taxYear: command.TaxYear,
                actorId: generation.TenantId,
                correlationId: correlation.CorrelationId,
                ct: ct
            );

            if (save.IsFailure)
            {
                await FailAsync(generation, command, save.Error, unitOfWork, bus, now, logger, ct);
                return;
            }

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
        IDocumentTemplateRenderer renderer,
        IHtmlToPdfConverter pdfConverter,
        IQrCodeGenerator qrGenerator,
        CancellationToken ct
    )
    {
        var data = BuildRenderData(command, branding, qrGenerator);

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

    // Fluid resuelve el acceso por clave (.number) solo sobre IDictionary<string, object> — por eso los
    // diccionarios son object (no object?) y los nulos se colapsan a "" antes de renderizar. Los montos
    // llegan ya calculados; acá solo se formatean (cultura invariante).
    private static IReadOnlyDictionary<string, object?> BuildRenderData(
        ProcessInvoiceGenerationCommand command,
        BrandingPayload? branding,
        IQrCodeGenerator qrGenerator
    )
    {
        var invoice = command.Invoice;
        var culture = CultureInfo.InvariantCulture;

        // El link de pago lo arma Billing con el subdominio del tenant; Documents solo lo acepta si es una
        // URL absoluta http/https (fail-closed: si no lo es, no se dibuja botón ni QR). El QR codifica esa
        // misma URL — hereda el subdominio automáticamente. Pagada ⇒ nunca se ofrece pago.
        var paymentUrl =
            IsRenderablePaymentUrl(invoice.PaymentUrl) && invoice.Status != "Paid" ? invoice.PaymentUrl! : string.Empty;
        var paymentQr = paymentUrl.Length > 0 ? qrGenerator.CreatePngDataUri(paymentUrl) : string.Empty;

        // Logo solo embebido (data:) — un recurso externo lo bloquea el CSP del motor.
        var logo =
            branding?.LogoDataUri is { Length: > 0 } l && l.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? l
                : string.Empty;
        var brandColor = IsHexColor(branding?.BrandColorHex) ? branding!.BrandColorHex! : "#2563eb";
        var displayName = string.IsNullOrWhiteSpace(branding?.DisplayName)
            ? invoice.Issuer.Name
            : branding!.DisplayName!;
        var footer = string.IsNullOrWhiteSpace(branding?.FooterText)
            ? $"Documento generado por TaxVision · Factura {command.InvoiceNumber}"
            : branding!.FooterText!;

        var lines = invoice
            .Lines.Select(
                object (line) =>
                    new Dictionary<string, object>
                    {
                        ["description"] = line.Description,
                        ["quantity"] = line.Quantity.ToString("0.##", culture),
                        ["unitPrice"] = line.UnitPrice.ToString("N2", culture),
                        ["amount"] = line.Amount.ToString("N2", culture),
                    }
            )
            .ToList();

        // Ajustes de onboarding (descuentos por código): una fila negativa por beneficio.
        var adjustments = (invoice.Adjustments ?? [])
            .Select(
                object (adj) =>
                    new Dictionary<string, object>
                    {
                        ["label"] = adj.Label,
                        ["amount"] = adj.Amount.ToString("N2", culture),
                    }
            )
            .ToList();

        return new Dictionary<string, object?>
        {
            ["invoice"] = new Dictionary<string, object>
            {
                ["number"] = command.InvoiceNumber,
                ["taxYear"] = command.TaxYear,
                ["currency"] = invoice.Currency,
                ["issueDate"] = invoice.IssueDate.ToString("yyyy-MM-dd", culture),
                ["dueDate"] = invoice.DueDate?.ToString("yyyy-MM-dd", culture) ?? string.Empty,
                ["issuer"] = PartyToDict(invoice.Issuer),
                ["customer"] = PartyToDict(invoice.Customer),
                ["lines"] = lines,
                ["subtotal"] = invoice.Subtotal.ToString("N2", culture),
                ["taxAmount"] = invoice.TaxAmount.ToString("N2", culture),
                ["total"] = invoice.Total.ToString("N2", culture),
                ["notes"] = invoice.Notes ?? string.Empty,
                // Onboarding con código: ajustes (negativos), descuento total y tipo de liquidación.
                ["adjustments"] = adjustments,
                ["discount"] = invoice.Discount.ToString("N2", culture),
                ["hasDiscount"] = invoice.Discount > 0m,
                ["settlementType"] = invoice.SettlementType ?? string.Empty,
                // Estado + cobro (dato de Billing). La plantilla decide watermark y visibilidad del botón.
                ["status"] = string.IsNullOrWhiteSpace(invoice.Status) ? "Pending" : invoice.Status,
                ["paidDate"] = invoice.PaidDate?.ToString("yyyy-MM-dd", culture) ?? string.Empty,
                ["paymentUrl"] = paymentUrl,
                ["paymentQr"] = paymentQr,
                // Comprobante (aparece en la versión Pagada): número de recibo + hash de verificación.
                ["receiptNumber"] = invoice.ReceiptNumber ?? string.Empty,
                ["receiptHash"] = invoice.ReceiptHash ?? string.Empty,
                // Branding del tenant (opcional). La plantilla los aplica; si vienen vacíos, look por defecto.
                ["logo"] = logo,
                ["brandColor"] = brandColor,
                ["displayName"] = displayName,
                ["footer"] = footer,
            },
        };
    }

    private static Dictionary<string, object> PartyToDict(InvoiceParty party) =>
        new()
        {
            ["name"] = party.Name,
            ["taxId"] = party.TaxId,
            ["address"] = party.Address ?? string.Empty,
        };

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

    // Merge campo a campo: lo que venga en el request gana; si no, el perfil guardado del tenant; si no,
    // null (BuildRenderData aplica el default por campo). Así el tenant configura una vez y puede
    // sobrescribir algo puntual en un request sin tocar su perfil.
    private static BrandingPayload? ResolveBranding(BrandingPayload? request, DocumentBranding? stored)
    {
        if (request is null && stored is null)
            return null;

        return new BrandingPayload(
            DisplayName: Coalesce(request?.DisplayName, stored?.DisplayName),
            LogoDataUri: Coalesce(request?.LogoDataUri, stored?.LogoDataUri),
            BrandColorHex: Coalesce(request?.BrandColorHex, stored?.BrandColorHex),
            FooterText: Coalesce(request?.FooterText, stored?.FooterText)
        );
    }

    private static string? Coalesce(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static bool IsRenderablePaymentUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsHexColor(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is 4 or 7
        && value[0] == '#'
        && value.AsSpan(1).ToArray().All(Uri.IsHexDigit);

    private static string ResolveCorrelationId(ProcessInvoiceGenerationCommand command) =>
        string.IsNullOrWhiteSpace(command.CorrelationId) ? command.GenerationId.ToString("N") : command.CorrelationId;
}
