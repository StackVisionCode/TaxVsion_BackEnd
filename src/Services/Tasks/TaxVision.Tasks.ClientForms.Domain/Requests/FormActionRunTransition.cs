namespace TaxVision.Tasks.ClientForms.Domain.Requests;

// Resultado de aplicar un resultado externo: Ignored cuando el run ya era terminal y no cambia.
public enum FormActionRunTransition
{
    Ignored = 0,
    Applied = 1,
}
