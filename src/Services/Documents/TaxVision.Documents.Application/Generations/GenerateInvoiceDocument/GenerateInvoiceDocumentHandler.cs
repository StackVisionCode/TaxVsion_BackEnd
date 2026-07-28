using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;

/// <summary>
/// Camino de request (202): registra la generación en estado Requested de forma idempotente y encola
/// la ejecución asíncrona (<see cref="ProcessInvoiceGenerationCommand"/>). No renderiza nada acá — la
/// llamada HTTP devuelve enseguida. El registro y el encolado se commitean juntos (outbox durable de
/// Wolverine): si la inserción choca contra la unique constraint de idempotencia, no se encola nada.
/// </summary>
public static class GenerateInvoiceDocumentHandler
{
    private const string OwnerTypeInvoice = "Invoice";
    private const string DocumentTypeInvoice = "Invoice";

    public static async Task<Result<GenerateInvoiceDocumentResult>> Handle(
        GenerateInvoiceDocumentCommand command,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<GenerateInvoiceDocumentResult> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? correlation.CorrelationId
            : command.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var validation = Validate(command);
            if (validation.IsFailure)
                return Result.Failure<GenerateInvoiceDocumentResult>(validation.Error);

            // Idempotencia: una solicitud repetida devuelve la generación existente (mismo 202), sin
            // volver a encolar el procesamiento.
            var existing = await repository.GetByIdempotencyKeyAsync(command.TenantId, command.IdempotencyKey, ct);
            if (existing is not null)
                return Result.Success(new GenerateInvoiceDocumentResult(existing.Id, existing.Status.ToString()));

            var buildResult = BuildGeneration(command, clock.GetUtcNow().UtcDateTime);
            if (buildResult.IsFailure)
                return Result.Failure<GenerateInvoiceDocumentResult>(buildResult.Error);

            var generation = buildResult.Value;
            await repository.AddAsync(generation, ct);

            try
            {
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (ConflictException)
            {
                // Carrera: otra solicitud idéntica ganó la unique constraint. Reusar la suya.
                var winner = await repository.GetByIdempotencyKeyAsync(command.TenantId, command.IdempotencyKey, ct);
                if (winner is not null)
                    return Result.Success(new GenerateInvoiceDocumentResult(winner.Id, winner.Status.ToString()));
                throw;
            }

            await bus.PublishAsync(ToProcessCommand(command, generation.Id, correlationId));

            logger.LogInformation(
                "Invoice document generation {GenerationId} accepted for invoice {InvoiceId} (tenant {TenantId}).",
                generation.Id,
                command.InvoiceId,
                command.TenantId
            );

            return Result.Success(
                new GenerateInvoiceDocumentResult(generation.Id, DocumentGenerationStatus.Requested.ToString())
            );
        }
    }

    private static Result Validate(GenerateInvoiceDocumentCommand command)
    {
        if (command.Invoice is null)
            return Result.Failure(new Error("Documents.Invoice.MissingPayload", "Invoice payload is required."));
        if (command.Invoice.Lines is null || command.Invoice.Lines.Count == 0)
            return Result.Failure(new Error("Documents.Invoice.NoLines", "An invoice must have at least one line."));
        if (string.IsNullOrWhiteSpace(command.InvoiceNumber))
            return Result.Failure(new Error("Documents.Invoice.MissingNumber", "InvoiceNumber is required."));
        if (command.TaxYear < 2000)
            return Result.Failure(new Error("Documents.Invoice.InvalidTaxYear", "TaxYear is required for invoices."));
        return Result.Success();
    }

    private static Result<DocumentGeneration> BuildGeneration(GenerateInvoiceDocumentCommand command, DateTime nowUtc)
    {
        var documentType = DocumentType.Create(DocumentTypeInvoice);
        if (documentType.IsFailure)
            return Result.Failure<DocumentGeneration>(documentType.Error);

        var templateKey = TemplateKey.Create(command.TemplateKey);
        if (templateKey.IsFailure)
            return Result.Failure<DocumentGeneration>(templateKey.Error);

        return DocumentGeneration.Request(
            tenantId: command.TenantId,
            documentType: documentType.Value,
            templateKey: templateKey.Value,
            templateVersion: command.TemplateVersion,
            outputFormat: DocumentOutputFormat.Pdf,
            owner: new GenerationOwner(OwnerTypeInvoice, command.InvoiceId),
            sourceService: command.SourceService,
            documentVersion: command.DocumentVersion,
            priority: DocumentPriority.Normal,
            idempotencyKey: command.IdempotencyKey,
            correlationId: command.CorrelationId,
            causationId: null,
            nowUtc: nowUtc
        );
    }

    private static ProcessInvoiceGenerationCommand ToProcessCommand(
        GenerateInvoiceDocumentCommand command,
        Guid generationId,
        string correlationId
    ) =>
        new(
            GenerationId: generationId,
            TenantId: command.TenantId,
            InvoiceNumber: command.InvoiceNumber,
            TemplateKey: command.TemplateKey,
            TemplateVersion: command.TemplateVersion,
            OwnerType: OwnerTypeInvoice,
            OwnerId: command.InvoiceId,
            DocumentVersion: command.DocumentVersion,
            TaxYear: command.TaxYear,
            FileName: $"invoice-{Sanitize(command.InvoiceNumber)}.pdf",
            CorrelationId: correlationId,
            Invoice: command.Invoice,
            Branding: command.Branding
        );

    private static string Sanitize(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        return new string(chars);
    }
}
