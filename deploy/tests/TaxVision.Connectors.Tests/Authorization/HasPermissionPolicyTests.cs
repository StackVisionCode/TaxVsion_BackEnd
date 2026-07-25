using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;

namespace TaxVision.Connectors.Tests.Authorization;

/// <summary>
/// Verifica la policy "perm:{permission}" (<see cref="HasPermissionAttribute"/> +
/// <see cref="PermissionPolicyProvider"/>) que gatea <c>AccountsController</c> — en particular
/// <see cref="ConnectorsPermissions.AccountsRead"/>/<see cref="ConnectorsPermissions.AccountsWrite"/>,
/// los dos permisos sembrados en Auth por la migración <c>AddConnectorsAccountPermissions</c>
/// (Fase 6.5 de hardening). Antes de esa migración estos permisos no existían como fila real en
/// la tabla de Auth, así que ningún token de usuario podía llevar el claim "perm" correspondiente
/// — este test prueba el mecanismo de autorización en sí (que ahora sí tiene a quién aplicarse).
///
/// No usamos WebApplicationFactory (sin precedente en el repo, mismo criterio que
/// <c>ServiceOnlyPolicyTests</c> de Customer.Tests): se resuelve la policy real vía
/// <see cref="PermissionPolicyProvider.GetPolicyAsync"/> (el mismo componente registrado en
/// Program.cs) y se evalúa su <see cref="AssertionRequirement"/> contra un
/// <see cref="AuthorizationHandlerContext"/> real, sin depender del pipeline HTTP completo.
/// </summary>
public sealed class HasPermissionPolicyTests
{
    private static readonly PermissionPolicyProvider Provider = new(Options.Create(new AuthorizationOptions()));

    private static async Task<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string permission)
    {
        var policy = await Provider.GetPolicyAsync($"{HasPermissionAttribute.PolicyPrefix}{permission}");
        Assert.NotNull(policy);

        // La policy tiene 2 requirements (RequireAuthenticatedUser + RequireAssertion). El único
        // que expresa el permiso en sí (el "perm" claim) es el AssertionRequirement — lo
        // evaluamos directamente (equivalente a lo que hace HasPermission real: el requirement de
        // "usuario autenticado" lo satisface el propio pipeline JWT antes de llegar a la policy).
        var assertionRequirement = policy!.Requirements.OfType<AssertionRequirement>().Single();
        var context = new AuthorizationHandlerContext([assertionRequirement], principal, resource: null);
        await assertionRequirement.HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) => new(new ClaimsIdentity(claims, "Bearer"));

    [Fact]
    public async Task Token_without_perm_claim_is_rejected_for_AccountsRead()
    {
        var principal = PrincipalWith(new Claim("tenant_id", Guid.NewGuid().ToString()));

        Assert.False(await IsAuthorizedAsync(principal, ConnectorsPermissions.AccountsRead));
    }

    [Fact]
    public async Task Token_with_perm_claim_AccountsRead_is_authorized_for_AccountsRead()
    {
        var principal = PrincipalWith(
            new Claim("perm", ConnectorsPermissions.AccountsRead),
            new Claim("tenant_id", Guid.NewGuid().ToString())
        );

        Assert.True(await IsAuthorizedAsync(principal, ConnectorsPermissions.AccountsRead));
    }

    [Fact]
    public async Task Token_with_perm_claim_AccountsRead_is_still_rejected_for_AccountsWrite()
    {
        // El claim "perm" es específico del permiso — tener accounts.read no habilita
        // accounts.write (Initiate/ConnectManual/Disconnect/AdminConsentUrl/Reauth).
        var principal = PrincipalWith(new Claim("perm", ConnectorsPermissions.AccountsRead));

        Assert.False(await IsAuthorizedAsync(principal, ConnectorsPermissions.AccountsWrite));
    }

    [Fact]
    public async Task Token_with_perm_claim_AccountsWrite_is_authorized_for_AccountsWrite()
    {
        var principal = PrincipalWith(new Claim("perm", ConnectorsPermissions.AccountsWrite));

        Assert.True(await IsAuthorizedAsync(principal, ConnectorsPermissions.AccountsWrite));
    }

    [Fact]
    public async Task TenantAdmin_role_without_perm_claim_is_rejected_for_AccountsWrite()
    {
        // Auditoría de seguridad post-hardening: TenantAdmin YA NO pasa por rol solo — Auth
        // computa su set completo de claims "perm" al login (PermissionCatalog.SystemRoleDefaults,
        // que sigue incluyendo accounts.write por defecto), así que el rol solo, sin el claim
        // real, ahora se rechaza (antes esto pasaba por el bypass de ClaimsPrincipalExtensions.
        // HasPermission, retirado tras encontrar que dejaba pasar signature.constraints.manage
        // — un permiso 100% PlatformOnly — a cualquier TenantAdmin).
        var principal = PrincipalWith(new Claim(ClaimTypes.Role, "TenantAdmin"));

        Assert.False(await IsAuthorizedAsync(principal, ConnectorsPermissions.AccountsWrite));
    }

    [Fact]
    public async Task TenantAdmin_role_with_perm_claim_is_authorized_for_AccountsWrite()
    {
        // El claim sí lo tiene en la práctica: Auth se lo otorga por defecto vía
        // PermissionCatalog.SystemRoleDefaults al emitir el JWT — este test simula ese JWT real.
        var principal = PrincipalWith(
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("perm", ConnectorsPermissions.AccountsWrite)
        );

        Assert.True(await IsAuthorizedAsync(principal, ConnectorsPermissions.AccountsWrite));
    }

    [Fact]
    public async Task PlatformAdmin_role_is_authorized_for_AccountsWrite_without_any_perm_claim()
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.Role, "PlatformAdmin"));

        Assert.True(await IsAuthorizedAsync(principal, ConnectorsPermissions.AccountsWrite));
    }

    [Fact]
    public async Task TenantEmployee_role_without_perm_claim_is_rejected_for_AccountsWrite()
    {
        // A diferencia de TenantAdmin, el rol por sí solo no basta — un empleado necesita el
        // claim "perm" real (que hoy Auth solo otorga si el token trae accounts.write asignado;
        // por defecto SystemEmployee NO lo tiene, ver PermissionCatalog.SystemRoleDefaults).
        var principal = PrincipalWith(new Claim(ClaimTypes.Role, "TenantEmployee"));

        Assert.False(await IsAuthorizedAsync(principal, ConnectorsPermissions.AccountsWrite));
    }
}
