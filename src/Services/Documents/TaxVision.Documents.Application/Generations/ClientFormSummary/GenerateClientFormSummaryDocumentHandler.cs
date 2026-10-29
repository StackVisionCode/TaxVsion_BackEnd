using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Documents.Application.Generations.ClientFormSummary;

/// <summary>
/// Camino de alta del Submission Summary. Registra la generación bajo el tenant real con owner según
/// el participante (Customer o FormParticipant) para que la autorización propia de CloudStorage alcance.
/// El GenerationId de Tasks se guarda como ExternalReferenceId (el PK propio es interno): en un reintento
/// Tasks rota su id con la misma clave de idempotencia y acá se reapunta. Molde: GenerateSaaSReceipt.
/// </summary>
public static class GenerateClientFormSummaryDocumentHandler
{
    public const string OwnerTypeCustomer = "Customer";
    public const string OwnerTypeParticipant = "FormParticipant";

    private static readonly TimeSpan StalledAfter = TimeSpan.FromMinutes(10);

    public static async Task<Result<GenerateClientFormSummaryDocumentResult>> Handle(
        GenerateClientFormSummaryDocumentCommand command,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<GenerateClientFormSummaryDocumentResult> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? correlation.CorrelationId
            : command.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var owner = ResolveOwner(command.Participant);
            if (owner is null)
                return Result.Failure<GenerateClientFormSummaryDocumentResult>(
                    new Error("Documents.ClientFormSummary.InvalidParticipant", "The participant owner is invalid.")
                );

            var nowUtc = clock.GetUtcNow().UtcDateTime;
            var existing = await repository.GetByIdempotencyKeyAsync(command.TenantId, command.IdempotencyKey, ct);
            if (existing is not null && !IsRetryable(existing, nowUtc))
                return Result.Success(
                    new GenerateClientFormSummaryDocumentResult(existing.Id, existing.Status.ToString())
                );

            if (existing is not null)
            {
                var retry = existing.RetryStalled(nowUtc);
                if (retry.IsFailure)
                    return Result.Failure<GenerateClientFormSummaryDocumentResult>(retry.Error);
                existing.SetExternalReference(command.GenerationId);

                await unitOfWork.SaveChangesAsync(ct);
                await bus.PublishAsync(ToProcessCommand(command, existing.Id, correlationId));
                return Result.Success(
                    new GenerateClientFormSummaryDocumentResult(existing.Id, DocumentGenerationStatus.Queued.ToString())
                );
            }

            var built = BuildGeneration(command, owner.Value, correlationId, nowUtc);
            if (built.IsFailure)
                return Result.Failure<GenerateClientFormSummaryDocumentResult>(built.Error);

            await repository.AddAsync(built.Value, ct);
            try
            {
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (ConflictException)
            {
                // Dos entregas del mismo evento a la vez: gana una y la otra devuelve esa.
                var winner = await repository.GetByIdempotencyKeyAsync(command.TenantId, command.IdempotencyKey, ct);
                if (winner is not null)
                    return Result.Success(
                        new GenerateClientFormSummaryDocumentResult(winner.Id, winner.Status.ToString())
                    );
                throw;
            }

            await bus.PublishAsync(ToProcessCommand(command, built.Value.Id, correlationId));
            logger.LogInformation(
                "Client form summary generation {GenerationId} accepted for request {RequestId} rev {Revision}.",
                built.Value.Id,
                command.FormRequestId,
                command.RevisionNumber
            );
            return Result.Success(
                new GenerateClientFormSummaryDocumentResult(
                    built.Value.Id,
                    DocumentGenerationStatus.Requested.ToString()
                )
            );
        }
    }

    private static bool IsRetryable(DocumentGeneration generation, DateTime nowUtc) =>
        generation.Status == DocumentGenerationStatus.Failed
        || (
            generation.Status != DocumentGenerationStatus.Completed
            && generation.Status != DocumentGenerationStatus.Cancelled
            && nowUtc - generation.UpdatedAtUtc >= StalledAfter
        );

    private static (string OwnerType, Guid OwnerId)? ResolveOwner(ClientFormParticipant participant)
    {
        if (participant.CustomerId is { } customerId && customerId != Guid.Empty)
            return (OwnerTypeCustomer, customerId);
        if (participant.ParticipantId is { } participantId && participantId != Guid.Empty)
            return (OwnerTypeParticipant, participantId);
        return null;
    }

    private static Result<DocumentGeneration> BuildGeneration(
        GenerateClientFormSummaryDocumentCommand command,
        (string OwnerType, Guid OwnerId) owner,
        string correlationId,
        DateTime nowUtc
    )
    {
        var documentType = DocumentType.Create(ClientFormSubmissionSummaryRequestedConsumer.DocumentType);
        if (documentType.IsFailure)
            return Result.Failure<DocumentGeneration>(documentType.Error);

        var templateKey = TemplateKey.Create(ClientFormSubmissionSummaryRequestedConsumer.TemplateKey);
        if (templateKey.IsFailure)
            return Result.Failure<DocumentGeneration>(templateKey.Error);

        return DocumentGeneration.Request(
            tenantId: command.TenantId,
            documentType: documentType.Value,
            templateKey: templateKey.Value,
            templateVersion: 1,
            outputFormat: DocumentOutputFormat.Pdf,
            owner: new GenerationOwner(owner.OwnerType, owner.OwnerId),
            sourceService: ClientFormSubmissionSummaryRequestedConsumer.SourceService,
            documentVersion: command.RevisionNumber < 1 ? 1 : command.RevisionNumber,
            priority: DocumentPriority.Normal,
            idempotencyKey: command.IdempotencyKey,
            correlationId: correlationId,
            causationId: null,
            nowUtc: nowUtc,
            externalReferenceId: command.GenerationId
        );
    }

    private static ProcessClientFormSummaryGenerationCommand ToProcessCommand(
        GenerateClientFormSummaryDocumentCommand command,
        Guid documentGenerationId,
        string correlationId
    ) =>
        new(
            documentGenerationId,
            command.TenantId,
            command.FormRequestId,
            command.RevisionNumber,
            command.Participant,
            command.TaxYear,
            command.IdempotencyKey,
            correlationId,
            command.Payload,
            command.SignatureCopy
        );
}
