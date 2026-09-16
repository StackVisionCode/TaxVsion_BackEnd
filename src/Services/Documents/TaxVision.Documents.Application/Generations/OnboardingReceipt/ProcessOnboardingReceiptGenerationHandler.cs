using System.Globalization;
using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Documents.Application.Generations.OnboardingReceipt;

/// <summary>
/// Ejecución asíncrona de la generación del recibo de onboarding — mismo pipeline que
/// ProcessInvoiceGenerationHandler (datos → HTML Fluid → PDF Chromium → PUT bucket temporal +
/// SaveFileRequested), sin branding por tenant (el emisor es la plataforma, vía
/// <see cref="IPlatformIssuerProvider"/>) y sin QR/link de pago (el recibo es de un pago YA
/// confirmado, no una invitación a pagar). Fallos son terminales y observables: nunca se lanza
/// excepción, se marca Failed y se publica DocumentGenerationFailed.
/// </summary>
public static class ProcessOnboardingReceiptGenerationHandler
{
    private const string DocumentTypeOnboardingReceipt = "OnboardingReceipt";
    private const string OwnerTypeOnboarding = "Onboarding";
    private const string FolderTypeReceipts = "Receipts";
    private const string PdfContentType = "application/pdf";

    public static async Task Handle(
        ProcessOnboardingReceiptGenerationCommand command,
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
        ILogger<ProcessOnboardingReceiptGenerationCommand> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(command)))
        {
            var generation = await repository.GetByIdAsync(PlatformTenant.Id, command.GenerationId, ct);
            if (generation is null)
            {
                logger.LogWarning(
                    "ProcessOnboardingReceiptGeneration: generation {GenerationId} not found; ignoring.",
                    command.GenerationId
                );
                return;
            }

            if (generation.Status is not (DocumentGenerationStatus.Requested or DocumentGenerationStatus.Queued))
            {
                logger.LogInformation(
                    "ProcessOnboardingReceiptGeneration: generation {GenerationId} already in {Status}; skipping.",
                    generation.Id,
                    generation.Status
                );
                return;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            generation.Queue(now);
            generation.StartRendering(now);

            // El logo de marca de la plataforma (Company settings → TenantBrands del tenant plataforma,
            // el que administra el PlatformAdmin) se baja on-demand como data URI, igual que la factura
            // resuelve el logo de su tenant. Best-effort ESTRICTO: el logo NUNCA debe tumbar la generación
            // del recibo (el pago ya está confirmado), así que cualquier fallo del resolver (proyección de
            // logo ausente, storage caído) cae al logo de config y, si tampoco, el recibo sale sin logo.
            string? brandLogo = null;
            try
            {
                brandLogo = await tenantLogoResolver.ResolveLogoDataUriAsync(PlatformTenant.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Onboarding receipt {GenerationId}: brand logo resolution failed; falling back to the configured logo.",
                    command.GenerationId
                );
            }

            var pdf = await RenderAndConvertAsync(command, issuerProvider.GetSnapshot(), brandLogo, renderer, pdfConverter, ct);
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

            // Persistir Uploading + FileId ANTES de que CloudStorage pueda responder FileAvailable
            // (mismo guardrail que Invoice: el consumer correlaciona por ese FileId).
            await unitOfWork.SaveChangesAsync(ct);

            await bus.PublishAsync(
                new DocumentGenerationStartedIntegrationEvent
                {
                    TenantId = generation.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    GenerationId = generation.Id,
                    DocumentType = DocumentTypeOnboardingReceipt,
                }
            );

            var save = await storageClient.RequestSaveAsync(
                tenantId: PlatformTenant.Id,
                fileId: fileId,
                content: pdf.Value,
                fileName: command.FileName,
                contentType: PdfContentType,
                ownerType: OwnerTypeOnboarding,
                ownerId: command.OnboardingId,
                folderType: FolderTypeReceipts,
                taxYear: command.Receipt.PaidAtUtc.Year,
                actorId: PlatformTenant.Id,
                correlationId: correlation.CorrelationId,
                ct: ct
            );

            if (save.IsFailure)
            {
                await FailAsync(generation, command, save.Error, unitOfWork, bus, now, logger, ct);
                return;
            }

            logger.LogInformation(
                "Onboarding receipt generation {GenerationId} rendered ({Bytes} bytes) and handed to CloudStorage as file {FileId}.",
                generation.Id,
                pdf.Value.LongLength,
                fileId
            );
        }
    }

    private static async Task<Result<byte[]>> RenderAndConvertAsync(
        ProcessOnboardingReceiptGenerationCommand command,
        IssuerSnapshot issuer,
        string? brandLogoDataUri,
        IDocumentTemplateRenderer renderer,
        IHtmlToPdfConverter pdfConverter,
        CancellationToken ct
    )
    {
        var data = BuildRenderData(command, issuer, brandLogoDataUri);

        var html = await renderer.RenderHtmlAsync(
            command.TemplateKey,
            command.TemplateVersion,
            PlatformTenant.Id,
            data,
            ct
        );
        if (html.IsFailure)
            return Result.Failure<byte[]>(html.Error);

        return await pdfConverter.ConvertAsync(html.Value, ct);
    }

    // Fluid resuelve el acceso por clave solo sobre IDictionary<string, object> — mismo guardrail que
    // BuildRenderData de Invoice. Los montos llegan en centavos (long); acá se formatean a decimal.
    private static IReadOnlyDictionary<string, object?> BuildRenderData(
        ProcessOnboardingReceiptGenerationCommand command,
        IssuerSnapshot issuer,
        string? brandLogoDataUri
    )
    {
        var receipt = command.Receipt;
        var culture = CultureInfo.InvariantCulture;
        var priceFormatted = (receipt.PricePaidCents / 100m).ToString("N2", culture);

        return new Dictionary<string, object?>
        {
            ["receipt"] = new Dictionary<string, object>
            {
                ["payerName"] = $"{receipt.PayerFirstName} {receipt.PayerLastName}".Trim(),
                ["payerEmail"] = receipt.PayerEmail,
                ["planName"] = receipt.PlanName,
                ["price"] = priceFormatted,
                ["currency"] = receipt.Currency,
                ["paidAt"] = receipt.PaidAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", culture),
                ["transactionReferenceMask"] = receipt.TransactionReferenceMask,
                ["paymentMethodMasked"] = receipt.PaymentMethodMasked ?? string.Empty,
                ["issuer"] = new Dictionary<string, object>
                {
                    ["name"] = issuer.Name,
                    ["taxId"] = issuer.TaxId,
                    ["addressLine1"] = issuer.AddressLine1,
                    ["city"] = issuer.City,
                    ["state"] = issuer.State,
                    ["postalCode"] = issuer.PostalCode,
                    ["country"] = issuer.Country,
                    ["phone"] = issuer.Phone,
                    ["email"] = issuer.Email,
                    ["website"] = issuer.Website,
                    ["logo"] = ResolveLogo(brandLogoDataUri, issuer.LogoDataUri),
                },
            },
        };
    }

    // Prefiere el logo de marca del tenant plataforma (dinámico, el que administra el PlatformAdmin);
    // si no hay, el de config; validando que sea un data: URI embebible (la plantilla lo omite si queda
    // vacío). Nunca falla el render por el logo — es best-effort.
    private static string ResolveLogo(string? brandLogoDataUri, string? configLogoDataUri)
    {
        foreach (var candidate in new[] { brandLogoDataUri, configLogoDataUri })
        {
            if (candidate is { Length: > 0 } l && l.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return l;
        }
        return string.Empty;
    }

    private static async Task FailAsync(
        DocumentGeneration generation,
        ProcessOnboardingReceiptGenerationCommand command,
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
                OwnerType = OwnerTypeOnboarding,
                OwnerId = command.OnboardingId,
                ErrorCode = error.Code,
            }
        );

        logger.LogWarning(
            "Onboarding receipt generation {GenerationId} failed: {ErrorCode} — {ErrorMessage}.",
            generation.Id,
            error.Code,
            error.Message
        );
    }

    private static string ResolveCorrelationId(ProcessOnboardingReceiptGenerationCommand command) =>
        string.IsNullOrWhiteSpace(command.CorrelationId) ? command.GenerationId.ToString("N") : command.CorrelationId;
}
