using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Api.Requests;

// ---------------------------------------------------------------------------
// Fase 6 (03_Plan_De_Fases.md §Fase 6) — DTOs de body. Enums serializados como string
// (JsonStringEnumConverter global, ver Program.cs Fase 0).
// ---------------------------------------------------------------------------

public sealed record CreateNoteRequest(
    string Html,
    NoteTargetType TargetType,
    Guid? TargetId,
    NoteVisibility Visibility,
    NoteColorKind? ColorKind
);

public sealed record UpdateNoteContentRequest(string Html);

public sealed record ChangeNoteVisibilityRequest(NoteVisibility Visibility);

public sealed record SetNoteColorRequest(NoteColorKind? ColorKind);

public sealed record AttachFileToNoteRequest(
    Guid CloudStorageFileId,
    string DisplayName,
    string ContentType,
    long SizeBytes
);
