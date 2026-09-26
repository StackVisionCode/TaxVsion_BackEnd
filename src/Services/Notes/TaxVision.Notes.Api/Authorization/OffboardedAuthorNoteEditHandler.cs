using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.ResourceAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Api.Authorization;

/// <summary>
/// Punto 3.2 — grieta DELIBERADA en la regla "editar contenido = solo autor" de la capa Fase 9
/// (<see cref="IsOwnerOrHasManageHandler{TResource}"/> de <c>Note</c>, registrado sin permiso de
/// override): deja EDITAR (<see cref="Operations.Update"/>) una nota cuyo AUTOR fue RETIRADO del
/// tenant a un staff con <c>notes.view_all</c>. ASP.NET Core evalúa la policy como OR entre handlers,
/// así que este solo hace <c>Succeed</c> en el caso override — el resto (autor no retirado = solo
/// autor) no se relaja. Aplica SOLO a <see cref="Operations.Update"/> (endpoints de contenido);
/// archivar/borrar tienen su propio override <c>notes.view_all</c> en la capa Application
/// (<c>NoteVisibilityPolicy.CanManage</c>) y no pasan por acá.
/// </summary>
public sealed class OffboardedAuthorNoteEditHandler(
    IUserPermissionsSource permissions,
    IOffboardedStaffRepository offboardedStaff
) : AuthorizationHandler<OperationAuthorizationRequirement, Note>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        Note resource
    )
    {
        if (requirement.Name != Operations.Update.Name)
            return;

        var user = context.User;
        if (!user.TryGetUserId(out var userId) || userId == resource.CreatedByUserId)
            return; // al propio autor ya lo resuelve el handler de ownership base

        if (!await permissions.HasPermissionAsync(user, NotesPermissions.ViewAll, CancellationToken.None))
            return;

        if (
            await offboardedStaff.IsOffboardedAsync(resource.TenantId, resource.CreatedByUserId, CancellationToken.None)
        )
            context.Succeed(requirement);
    }
}
