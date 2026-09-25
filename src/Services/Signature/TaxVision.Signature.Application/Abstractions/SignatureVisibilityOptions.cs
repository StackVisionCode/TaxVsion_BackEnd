namespace TaxVision.Signature.Application.Abstractions;

// Flag de visibilidad por asignación (default false = todos ven todas las solicitudes hasta sembrar la
// proyección con la reconciliación). Espejo de Customers:AssignmentVisibility. Encender por entorno.
public sealed class SignatureVisibilityOptions
{
    public const string SectionName = "Signature:AssignmentVisibility";
    public bool Enabled { get; set; }
}
