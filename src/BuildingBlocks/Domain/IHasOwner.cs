namespace BuildingBlocks.Domain;

/// <summary>
/// RBAC Fase 4 (RBAC_Hardening_Plan.md) — marca un aggregate root como "propiedad" de un usuario
/// específico, habilitando el patrón de resource-based authorization de ASP.NET Core
/// (<c>IAuthorizationService.AuthorizeAsync(user, resource, requirement)</c> +
/// <c>IsOwnerOrHasManageHandler&lt;TResource&gt;</c>, ver
/// <c>BuildingBlocks.Web.ResourceAuthorization</c>). Vive en el proyecto base (no en
/// BuildingBlocks.Web) a propósito: los aggregates de Domain (ShareLink, SignatureRequest, Draft)
/// no pueden referenciar BuildingBlocks.Web (trae FrameworkReference a Microsoft.AspNetCore.App) —
/// mismo criterio que <see cref="ITenantOwned"/>, que vive acá por la misma razón.
///
/// Cuando el creador original no se conoce (filas legacy previas a que el aggregate empezara a
/// capturar este campo), la convención es <see cref="Guid.Empty"/> — nunca coincide con un userId
/// real del JWT, así que el chequeo de ownership falla-cerrado para esas filas en vez de requerir
/// un tipo nullable.
/// </summary>
public interface IHasOwner
{
    Guid CreatedByUserId { get; }
}
