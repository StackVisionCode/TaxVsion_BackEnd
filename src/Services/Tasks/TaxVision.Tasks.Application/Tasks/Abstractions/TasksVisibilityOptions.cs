namespace TaxVision.Tasks.Application.Tasks.Abstractions;

// Flag de visibilidad por asignación (default false = todos ven todas las tareas hasta sembrar la proyección
// con la reconciliación). Espejo de Customers:AssignmentVisibility. Encender por entorno tras sembrar.
public sealed class TasksVisibilityOptions
{
    public const string SectionName = "Tasks:AssignmentVisibility";
    public bool Enabled { get; set; }
}
