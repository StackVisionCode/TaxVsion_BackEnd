namespace TaxVision.Notes.Domain.Notes;

/// <summary>
/// Visibilidad por-nota (no por-rol). <c>ClientVisible</c> es lo único que el portal de clientes
/// puede leer (permiso <c>notes.portal.read</c>); un tenant employee nunca ve notas de otros
/// autores salvo con el permiso explícito <c>notes.view_all</c> (aplicado en Application/Api, no
/// en el dominio).
/// </summary>
public enum NoteVisibility
{
    Private = 0,
    Team = 1,
    ClientVisible = 2,
}
