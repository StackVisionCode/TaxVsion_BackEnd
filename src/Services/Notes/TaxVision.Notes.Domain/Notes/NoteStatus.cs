namespace TaxVision.Notes.Domain.Notes;

/// <summary>
/// Ciclo de vida de <see cref="Note"/>: <c>Active → Archived → Active</c> (reversible) y
/// <c>→ Deleted</c> (terminal, oculto salvo governance). Sin <c>ChangeStatus(x)</c> genérico —
/// cada transición es su propio método explícito en el aggregate.
/// </summary>
public enum NoteStatus
{
    Active = 0,
    Archived = 1,
    Deleted = 2,
}
