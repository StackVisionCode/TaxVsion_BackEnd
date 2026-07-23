using Microsoft.AspNetCore.Authorization;

namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Autorización por permiso: <c>[HasPermission(CustomersPermissions.FiscalProfileReveal)]</c>.
/// Implementación única, compartida por los microservicios — antes vivía copiada byte a byte en
/// cada uno (<c>Api/Authorization/PermissionAuthorization.cs</c>); ver Fase 1 y Fase 3 de
/// Actor_Type_Authorization_Layers_Plan.md. RBAC Fase 7.5: Growth también migró a este atributo
/// (su mecanismo M2M de <c>service-scope:</c> es aparte y sigue intacto — ver
/// <c>GrowthAuthorizationPolicyProvider</c>, que delega la rama <c>perm:</c> a
/// <see cref="PermissionPolicyProvider"/>).
/// </summary>
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public HasPermissionAttribute(string permission)
        : base($"{PolicyPrefix}{permission}") { }
}
