using BuildingBlocks.Common;
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
/// Camino de request (202), mismo patrón que GenerateInvoiceDocumentHandler: registra la generación
/// como Requested de forma idempotente y encola ProcessOnboardingReceiptGenerationCommand (outbox
/// durable — si la inserción choca contra la unique constraint de idempotencia, no se encola nada).
///
/// La generación se registra bajo <see cref="PlatformTenant.Id"/>, NUNCA bajo un tenant real: el
/// onboarding paga ANTES de que su tenant exista. Es el mismo mecanismo que ya usa Scribe para sus
/// propios assets — CloudStorage ya tiene una cuota de storage pre-provisionada para ese tenant
/// (PlatformStorageLimitBootstrapper), así que este slice no necesita tocar CloudStorage.
/// </summary>
public static class GenerateOnboardingReceiptDocumentHandler
{
    private const string OwnerTypeOnboarding = "Onboarding";
    private const string DocumentTypeOnboardingReceipt = "OnboardingReceipt";

    public static async Task<Result<GenerateOnboardingReceiptDocumentResult>> Handle(
        GenerateOnboardingReceiptDocumentCommand command,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<GenerateOnboardingReceiptDocumentResult> logger,
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
                return Result.Failure<GenerateOnboardingReceiptDocumentResult>(validation.Error);

            var existing = await repository.GetByIdempotencyKeyAsync(PlatformTenant.Id, command.IdempotencyKey, ct);
            if (existing is not null && existing.Status != DocumentGenerationStatus.Failed)
                return Result.Success(
                    new GenerateOnboardingReceiptDocumentResult(existing.Id, existing.Status.ToString())
                );

            if (existing is not null)
            {
                var retry = existing.RetryFromFailure(clock.GetUtcNow().UtcDateTime);
                if (retry.IsFailure)
                    return Result.Failure<GenerateOnboardingReceiptDocumentResult>(retry.Error);

                await unitOfWork.SaveChangesAsync(ct);
                await bus.PublishAsync(ToProcessCommand(command, existing.Id, correlationId));

                logger.LogInformation(
                    "Onboarding receipt document generation {GenerationId} re-queued for onboarding {OnboardingId}.",
                    existing.Id,
                    command.OnboardingId
                );

                return Result.Success(
                    new GenerateOnboardingReceiptDocumentResult(existing.Id, DocumentGenerationStatus.Queued.ToString())
                );
            }

            var buildResult = BuildGeneration(command, clock.GetUtcNow().UtcDateTime);
            if (buildResult.IsFailure)
                return Result.Failure<GenerateOnboardingReceiptDocumentResult>(buildResult.Error);

            var generation = buildResult.Value;
            await repository.AddAsync(generation, ct);

            try
            {
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (ConflictException)
            {
                var winner = await repository.GetByIdempotencyKeyAsync(PlatformTenant.Id, command.IdempotencyKey, ct);
                if (winner is not null)
                    return Result.Success(
                        new GenerateOnboardingReceiptDocumentResult(winner.Id, winner.Status.ToString())
                    );
                throw;
            }

            await bus.PublishAsync(ToProcessCommand(command, generation.Id, correlationId));

            logger.LogInformation(
                "Onboarding receipt document generation {GenerationId} accepted for onboarding {OnboardingId}.",
                generation.Id,
                command.OnboardingId
            );

            return Result.Success(
                new GenerateOnboardingReceiptDocumentResult(
                    generation.Id,
                    DocumentGenerationStatus.Requested.ToString()
                )
            );
        }
    }

    private static Result Validate(GenerateOnboardingReceiptDocumentCommand command)
    {
        if (command.Receipt is null)
            return Result.Failure(
                new Error("Documents.OnboardingReceipt.MissingPayload", "Receipt payload is required.")
            );
        if (string.IsNullOrWhiteSpace(command.Receipt.PayerEmail))
            return Result.Failure(
                new Error("Documents.OnboardingReceipt.MissingPayerEmail", "PayerEmail is required.")
            );
        if (string.IsNullOrWhiteSpace(command.Receipt.PlanName))
            return Result.Failure(new Error("Documents.OnboardingReceipt.MissingPlanName", "PlanName is required."));
        if (command.Receipt.PricePaidCents < 0)
            return Result.Failure(
                new Error("Documents.OnboardingReceipt.InvalidPrice", "PricePaidCents cannot be negative.")
            );
        if (string.IsNullOrWhiteSpace(command.Receipt.Currency))
            return Result.Failure(new Error("Documents.OnboardingReceipt.MissingCurrency", "Currency is required."));
        return Result.Success();
    }

    private static Result<DocumentGeneration> BuildGeneration(
        GenerateOnboardingReceiptDocumentCommand command,
        DateTime nowUtc
    )
    {
        var documentType = DocumentType.Create(DocumentTypeOnboardingReceipt);
        if (documentType.IsFailure)
            return Result.Failure<DocumentGeneration>(documentType.Error);

        var templateKey = TemplateKey.Create(command.TemplateKey);
        if (templateKey.IsFailure)
            return Result.Failure<DocumentGeneration>(templateKey.Error);

        return DocumentGeneration.Request(
            tenantId: PlatformTenant.Id,
            documentType: documentType.Value,
            templateKey: templateKey.Value,
            templateVersion: command.TemplateVersion,
            outputFormat: DocumentOutputFormat.Pdf,
            owner: new GenerationOwner(OwnerTypeOnboarding, command.OnboardingId),
            sourceService: command.SourceService,
            documentVersion: command.DocumentVersion,
            priority: DocumentPriority.High,
            idempotencyKey: command.IdempotencyKey,
            correlationId: command.CorrelationId,
            causationId: null,
            nowUtc: nowUtc
        );
    }

    private static ProcessOnboardingReceiptGenerationCommand ToProcessCommand(
        GenerateOnboardingReceiptDocumentCommand command,
        Guid generationId,
        string correlationId
    ) =>
        new(
            GenerationId: generationId,
            TemplateKey: command.TemplateKey,
            TemplateVersion: command.TemplateVersion,
            OnboardingId: command.OnboardingId,
            DocumentVersion: command.DocumentVersion,
            FileName: $"receipt-{command.OnboardingId:N}.pdf",
            CorrelationId: correlationId,
            Receipt: command.Receipt
        );
}
