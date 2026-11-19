using TaxVision.Notes.Application.Notes.Abstractions;
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

/// <summary>Query string de <c>GET /notes/feed</c> (contrato §3). Fechas UTC, rango [From, To) sobre UpdatedAtUtc.</summary>
public sealed record NoteFeedRequest
{
    public string? Q { get; init; }
    public NoteFeedScope Scope { get; init; } = NoteFeedScope.All;
    public Guid? CustomerId { get; init; }
    public Guid? AuthorUserId { get; init; }
    public NoteTargetType? TargetType { get; init; }
    public NoteFeedStatus Status { get; init; } = NoteFeedStatus.Active;
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public NoteFeedSort Sort { get; init; } = NoteFeedSort.Updated;
    public int Page { get; init; }
    public int Size { get; init; }

    // Valores numéricos fuera del enum también se rechazan.
    public bool HasValidEnums() =>
        Enum.IsDefined(Scope)
        && Enum.IsDefined(Status)
        && Enum.IsDefined(Sort)
        && (TargetType is null || Enum.IsDefined(TargetType.Value));

    public NoteFeedCriteria ToCriteria() =>
        new(Q, Scope, CustomerId, AuthorUserId, TargetType, Status, From?.UtcDateTime, To?.UtcDateTime, Sort);
}
