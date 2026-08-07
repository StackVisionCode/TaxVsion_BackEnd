using BuildingBlocks.Common;
using BuildingBlocks.Messaging.NotesIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Notes.Application.Notes.Commands;

public sealed record CreateNoteCommand(
    Guid TenantId,
    Guid AuthorUserId,
    string RawHtml,
    NoteTargetType TargetType,
    Guid? TargetId,
    NoteVisibility Visibility,
    NoteColorKind? ColorKind
);

/// <summary>
/// 03_Plan_De_Fases.md §Fase 5 — la validación de <c>TargetType == Customer</c> contra
/// <see cref="ICustomerDirectoryRepository"/> es SOFT (02_Contratos §5.5): nunca bloquea la
/// creación, nunca hace HTTP síncrono a Customer. Solo loguea si la proyección todavía no conoce
/// ese customer (import reciente / proyección atrasada) — el frontend ya lo eligió de una lista
/// real, así que se confía en él.
/// </summary>
public static class CreateNoteHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        CreateNoteCommand command,
        INoteRepository notes,
        ICustomerDirectoryRepository customerDirectory,
        IHtmlSanitizer sanitizer,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<Note> logger,
        CancellationToken ct
    )
    {
        await SoftVerifyCustomerReferenceAsync(command, customerDirectory, logger, ct);

        var referenceResult = NoteReference.Create(command.TargetType, command.TargetId);
        if (referenceResult.IsFailure)
            return Result.Failure<NoteResponse>(referenceResult.Error);

        var contentResult = BuildContent(command, sanitizer);
        if (contentResult.IsFailure)
            return Result.Failure<NoteResponse>(contentResult.Error);

        var colorResult = BuildColor(command);
        if (colorResult.IsFailure)
            return Result.Failure<NoteResponse>(colorResult.Error);

        var noteResult = Note.Create(
            command.TenantId,
            command.AuthorUserId,
            contentResult.Value,
            referenceResult.Value,
            command.Visibility,
            colorResult.Value
        );
        if (noteResult.IsFailure)
            return Result.Failure<NoteResponse>(noteResult.Error);

        var note = noteResult.Value;
        await notes.AddAsync(note, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new NoteCreatedIntegrationEvent
            {
                TenantId = note.TenantId,
                CorrelationId = correlation.CorrelationId,
                NoteId = note.Id,
                AuthorUserId = note.CreatedByUserId,
                TargetType = note.Reference.TargetType.ToString(),
                TargetId = note.Reference.TargetId,
                Visibility = note.Visibility.ToString(),
                CreatedAtUtc = note.CreatedAtUtc,
            }
        );

        return Result.Success(NoteResponse.From(note));
    }

    private static async Task SoftVerifyCustomerReferenceAsync(
        CreateNoteCommand command,
        ICustomerDirectoryRepository customerDirectory,
        ILogger<Note> logger,
        CancellationToken ct
    )
    {
        if (command.TargetType != NoteTargetType.Customer || command.TargetId is not { } customerId)
            return;

        var exists = await customerDirectory.ExistsAsync(command.TenantId, customerId, ct);
        if (!exists)
            logger.LogInformation(
                "CreateNote references customer {CustomerId} not yet present in CustomerDirectoryEntries for tenant {TenantId} — allowing anyway (soft validation, projection may lag).",
                customerId,
                command.TenantId
            );
    }

    private static Result<NoteContent> BuildContent(CreateNoteCommand command, IHtmlSanitizer sanitizer) =>
        NoteContent.Create(sanitizer.Sanitize(command.RawHtml));

    private static Result<NoteColor?> BuildColor(CreateNoteCommand command)
    {
        if (command.ColorKind is not { } kind)
            return Result.Success<NoteColor?>(null);

        var colorResult = NoteColor.Create(kind);
        return colorResult.IsFailure
            ? Result.Failure<NoteColor?>(colorResult.Error)
            : Result.Success<NoteColor?>(colorResult.Value);
    }
}
