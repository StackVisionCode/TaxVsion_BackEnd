using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Documents.Application.Generations.SaaSReceipt;

/// <summary>
/// Camino de request del recibo de una compra SaaS. Molde: <c>GenerateOnboardingReceiptDocumentHandler</c>,
/// con una diferencia que importa: la generación se registra bajo el **tenant real**, no bajo el de
/// plataforma. Así el archivo termina siendo suyo y la autorización propia de CloudStorage alcanza para que
/// su administrador lo descargue, sin tokens de plataforma de por medio.
/// </summary>
public static class GenerateSaaSReceiptDocumentHandler
{
    public const string OwnerTypeSaaSPayment = "SaaSPayment";
    public const string DocumentTypeSaaSReceipt = "SaaSReceipt";

    public static async Task<Result<GenerateSaaSReceiptDocumentResult>> Handle(
        GenerateSaaSReceiptDocumentCommand command,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<GenerateSaaSReceiptDocumentResult> logger,
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
                return Result.Failure<GenerateSaaSReceiptDocumentResult>(validation.Error);

            var nowUtc = clock.GetUtcNow().UtcDateTime;
            var existing = await repository.GetByIdempotencyKeyAsync(command.TenantId, command.IdempotencyKey, ct);
            if (existing is not null && !IsRetryable(existing, nowUtc))
                return Result.Success(new GenerateSaaSReceiptDocumentResult(existing.Id, existing.Status.ToString()));

            if (existing is not null)
            {
                var retry = existing.RetryStalled(nowUtc);
                if (retry.IsFailure)
                    return Result.Failure<GenerateSaaSReceiptDocumentResult>(retry.Error);

                await unitOfWork.SaveChangesAsync(ct);
                await bus.PublishAsync(ToProcessCommand(command, existing.Id, correlationId));

                return Result.Success(
                    new GenerateSaaSReceiptDocumentResult(existing.Id, DocumentGenerationStatus.Queued.ToString())
                );
            }

            var built = BuildGeneration(command, nowUtc);
            if (built.IsFailure)
                return Result.Failure<GenerateSaaSReceiptDocumentResult>(built.Error);

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
                    return Result.Success(new GenerateSaaSReceiptDocumentResult(winner.Id, winner.Status.ToString()));
                throw;
            }

            await bus.PublishAsync(ToProcessCommand(command, built.Value.Id, correlationId));

            logger.LogInformation(
                "SaaS receipt generation {GenerationId} accepted for payment {PaymentId} of tenant {TenantId}.",
                built.Value.Id,
                command.SaaSPaymentId,
                command.TenantId
            );

            return Result.Success(
                new GenerateSaaSReceiptDocumentResult(built.Value.Id, DocumentGenerationStatus.Requested.ToString())
            );
        }
    }

    /// <summary>Cuánto se espera antes de dar por atascada una generación a medias.</summary>
    private static readonly TimeSpan StalledAfter = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Una entrega repetida del mismo evento no debe rehacer nada, pero un pedido que llega mucho después
    /// —el del backfill— significa que el recibo nunca apareció. Falló, o se quedó a mitad hace rato: en los
    /// dos casos se vuelve a intentar. Lo ya completado nunca.
    /// </summary>
    private static bool IsRetryable(DocumentGeneration generation, DateTime nowUtc) =>
        generation.Status == DocumentGenerationStatus.Failed
        || (
            generation.Status != DocumentGenerationStatus.Completed
            && generation.Status != DocumentGenerationStatus.Cancelled
            && nowUtc - generation.UpdatedAtUtc >= StalledAfter
        );

    private static Result Validate(GenerateSaaSReceiptDocumentCommand command)
    {
        if (command.TenantId == Guid.Empty)
            return Result.Failure(new Error("Documents.SaaSReceipt.MissingTenant", "TenantId is required."));
        if (command.Receipt is null)
            return Result.Failure(new Error("Documents.SaaSReceipt.MissingPayload", "Receipt payload is required."));
        if (string.IsNullOrWhiteSpace(command.Receipt.Description))
            return Result.Failure(new Error("Documents.SaaSReceipt.MissingDescription", "Description is required."));
        if (command.Receipt.AmountPaidCents <= 0)
            return Result.Failure(
                new Error("Documents.SaaSReceipt.InvalidAmount", "AmountPaidCents must be greater than zero.")
            );
        if (string.IsNullOrWhiteSpace(command.Receipt.Currency))
            return Result.Failure(new Error("Documents.SaaSReceipt.MissingCurrency", "Currency is required."));
        return Result.Success();
    }

    private static Result<DocumentGeneration> BuildGeneration(
        GenerateSaaSReceiptDocumentCommand command,
        DateTime nowUtc
    )
    {
        var documentType = DocumentType.Create(DocumentTypeSaaSReceipt);
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
            owner: new GenerationOwner(OwnerTypeSaaSPayment, command.SaaSPaymentId),
            sourceService: command.SourceService,
            documentVersion: 1,
            priority: DocumentPriority.Normal,
            idempotencyKey: command.IdempotencyKey,
            correlationId: command.CorrelationId,
            causationId: null,
            nowUtc: nowUtc
        );
    }

    private static ProcessSaaSReceiptGenerationCommand ToProcessCommand(
        GenerateSaaSReceiptDocumentCommand command,
        Guid generationId,
        string correlationId
    ) =>
        new(
            GenerationId: generationId,
            TenantId: command.TenantId,
            SaaSPaymentId: command.SaaSPaymentId,
            TemplateKey: command.TemplateKey,
            TemplateVersion: command.TemplateVersion,
            FileName: $"TaxProffice_Receipt_{command.Receipt.PaidAtUtc:yyyy-MM-dd}.pdf",
            CorrelationId: correlationId,
            Receipt: command.Receipt
        );
}
