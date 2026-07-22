using Microsoft.AspNetCore.Authorization;

namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Autorización por permiso: <c>[HasPermission(CustomersPermissions.FiscalProfileReveal)]</c>.
/// Implementación única, compartida por los microservicios — antes vivía copiada byte a byte en
/// cada uno (<c>Api/Authorization/PermissionAuthorization.cs</c>); ver Fase 1 y Fase 3 de
/// Actor_Type_Authorization_Layers_Plan.md. Growth usa un mecanismo aparte (scopes M2M) y no
/// migra a este atributo.
/// </summary>
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public HasPermissionAttribute(string permission)
        : base($"{PolicyPrefix}{permission}") { }
}
