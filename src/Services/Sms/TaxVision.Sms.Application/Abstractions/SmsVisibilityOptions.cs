namespace TaxVision.Sms.Application.Abstractions;

// Flag de visibilidad por asignación (default false = todos ven todos los SMS hasta sembrar la proyección
// con la reconciliación). Espejo de Customers:AssignmentVisibility. Encender por entorno tras sembrar.
public sealed class SmsVisibilityOptions
{
    public const string SectionName = "Sms:AssignmentVisibility";
    public bool Enabled { get; set; }
}
