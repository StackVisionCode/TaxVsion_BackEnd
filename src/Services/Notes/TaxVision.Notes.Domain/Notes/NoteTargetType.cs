namespace TaxVision.Notes.Domain.Notes;

/// <summary>
/// Target polimórfico de una nota (ADR-03, 01_Modelo_De_Dominio.md §2.1) — 1 target por nota.
/// La existencia del target NO se valida aquí (cross-context); se resuelve por proyección/BFF
/// (ADR-09, Fase 4B para Customer).
/// </summary>
public enum NoteTargetType
{
    None = 0,
    Customer = 1,
    Task = 2,
    Appointment = 3,
    Meeting = 4,
    SignatureRequest = 5,
    Employee = 6,
    TaxCase = 7,
    Tenant = 8,
}
