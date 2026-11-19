using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes.Abstractions;

/// <summary>Alcance del feed de la central de notas (contrato §3).</summary>
public enum NoteFeedScope
{
    All = 0,
    Mine = 1,
    Team = 2,
    Client = 3,
    Private = 4,
    OthersPrivate = 5,
}

public enum NoteFeedStatus
{
    Active = 0,
    Archived = 1,
}

public enum NoteFeedSort
{
    Updated = 0,
    Created = 1,
    Pinned = 2,
}

/// <summary>Filtros del feed. Fechas UTC, rango semiabierto [FromUtc, ToUtc) sobre UpdatedAtUtc.</summary>
public sealed record NoteFeedCriteria(
    string? Term = null,
    NoteFeedScope Scope = NoteFeedScope.All,
    Guid? CustomerId = null,
    Guid? AuthorUserId = null,
    NoteTargetType? TargetType = null,
    NoteFeedStatus Status = NoteFeedStatus.Active,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    NoteFeedSort Sort = NoteFeedSort.Updated
);

/// <summary>Fila del feed: la nota y el nombre del cliente desde el directorio local (null si no aplica).</summary>
public sealed record NoteFeedRow(Note Note, string? CustomerName);
