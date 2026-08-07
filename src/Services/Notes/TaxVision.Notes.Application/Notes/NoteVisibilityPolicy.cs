using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes;

/// <summary>
/// Regla de visibilidad/autoría por-nota (03_Plan_De_Fases.md §Fase 5) — vive en Application, NUNCA
/// en el aggregate (guardrail 4: <see cref="Note"/> no conoce roles ni permisos). El chequeo grueso
/// "¿este actor puede llegar a este endpoint?" (staff con <c>notes.read</c>, portal con
/// <c>notes.portal.read</c>) ya lo resolvió la Capa 4b de authz en el controller (Fase 6) antes de
/// que un handler llegue a usar esta clase — lo que queda acá es la regla FINA por-nota (autoría +
/// <c>notes.view_all</c>) que ningún atributo declarativo puede expresar.
///
/// <para>
/// El path del CustomerPortal (<c>ListClientVisibleNotesQuery</c>) no pasa por
/// <see cref="CanStaffView"/> — un portal solo puede ver <see cref="NoteVisibility.ClientVisible"/>
/// nunca <c>Deleted</c>, y eso se filtra directo en el repo (más barato que traer todo y filtrar en
/// memoria), ver <c>ListClientVisibleNotesHandler</c>.
/// </para>
/// </summary>
public static class NoteVisibilityPolicy
{
    /// <summary>
    /// Puede ver la nota un actor staff (TenantEmployee/TenantAdmin/PlatformAdmin) ya autorizado a
    /// llegar al endpoint (tiene <c>notes.read</c>). <c>Team</c>/<c>ClientVisible</c> son visibles
    /// para cualquier staff; <c>Private</c> solo para el autor o quien tenga <c>notes.view_all</c>
    /// (governance). Una nota <c>Deleted</c> solo la ve quien tiene <c>notes.view_all</c>.
    /// </summary>
    public static bool CanStaffView(Note note, Guid actorUserId, bool actorHasViewAll)
    {
        if (note.Status == NoteStatus.Deleted)
            return actorHasViewAll;

        return note.Visibility switch
        {
            NoteVisibility.ClientVisible or NoteVisibility.Team => true,
            NoteVisibility.Private => note.CreatedByUserId == actorUserId || actorHasViewAll,
            _ => false,
        };
    }

    /// <summary>
    /// Solo el autor toca la nota — ni siquiera <c>notes.view_all</c> lo habilita (governance: admin
    /// ve y archiva, no edita ajena). Cubre contenido, visibilidad, pin/unpin, color y adjuntos.
    /// </summary>
    public static bool CanEditContent(Note note, Guid actorUserId) => note.CreatedByUserId == actorUserId;

    /// <summary>Ciclo de vida (archivar/restaurar/borrar): autor, o staff con <c>notes.view_all</c> (governance).</summary>
    public static bool CanManage(Note note, Guid actorUserId, bool actorHasViewAll) =>
        note.CreatedByUserId == actorUserId || actorHasViewAll;
}
