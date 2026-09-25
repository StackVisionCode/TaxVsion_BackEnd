namespace TaxVision.Calendar.Application.Appointments.Abstractions;

// Flag de visibilidad por asignación (default false = todos ven todas las citas hasta sembrar la proyección
// con la reconciliación). Espejo de Customers:AssignmentVisibility. Encender por entorno tras sembrar.
public sealed class CalendarVisibilityOptions
{
    public const string SectionName = "Calendar:AssignmentVisibility";
    public bool Enabled { get; set; }
}
