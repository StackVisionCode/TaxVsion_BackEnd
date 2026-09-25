namespace TaxVision.Notes.Application.Notes;

// Flag de visibilidad por asignación (default false = todas las notas de un cliente se rigen solo por la
// visibilidad por-nota existente hasta sembrar la proyección con la reconciliación). Espejo de
// Customers:AssignmentVisibility. Encender por entorno tras sembrar. Es ORTOGONAL a notes.view_all: esto
// acota las notas cuyo target es un Customer al staff asignado a ese cliente (bypass customers.view_all).
public sealed class NotesVisibilityOptions
{
    public const string SectionName = "Notes:AssignmentVisibility";
    public bool Enabled { get; set; }
}
