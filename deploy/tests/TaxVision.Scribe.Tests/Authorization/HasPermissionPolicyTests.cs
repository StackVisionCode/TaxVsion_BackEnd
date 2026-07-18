using System.Security.Claims;
using BuildingBlocks.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;
using TaxVision.Scribe.Api.Authorization;

namespace TaxVision.Scribe.Tests.Authorization;

/// <summary>
/// Verifica la policy "perm:{permission}" (<see cref="HasPermissionAttribute"/> +
/// <see cref="PermissionPolicyProvider"/>) que gatea los 4 controllers de Scribe — en particular
/// <see cref="ScribePermissions.TemplatesRead"/>/<see cref="ScribePermissions.TemplatesWrite"/>,
/// dos de los 9 permisos sembrados en Auth por la migración <c>AddScribePermissions</c> (Fase 10.5
/// de hardening, mismo gap y mismo fix que Connectors Fase 6.5). Antes de esa migración ninguno de
/// los 9 <c>scribe.*</c> existía como fila real en la tabla de Auth, así que ningún token (humano o
/// de servicio) podía llevar el claim "perm" correspondiente — este test prueba el mecanismo de
/// autorización en sí (que ahora sí tiene a quién aplicarse), mismo criterio y mismo caso de prueba
/// que <c>HasPermissionPolicyTests</c> de Connectors.Tests.
///
/// No usamos WebApplicationFactory (sin precedente en el repo): se resuelve la policy real vía
/// <see cref="PermissionPolicyProvider.GetPolicyAsync"/> (el mismo componente registrado en
/// Scribe.Api Program.cs) y se evalúa su <see cref="AssertionRequirement"/> contra un
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
    public async Task Token_without_perm_claim_is_rejected_for_TemplatesRead()
    {
        var principal = PrincipalWith(new Claim("tenant_id", Guid.NewGuid().ToString()));

        Assert.False(await IsAuthorizedAsync(principal, ScribePermissions.TemplatesRead));
    }

    [Fact]
    public async Task Token_with_perm_claim_TemplatesRead_is_authorized_for_TemplatesRead()
    {
        var principal = PrincipalWith(
            new Claim("perm", ScribePermissions.TemplatesRead),
            new Claim("tenant_id", Guid.NewGuid().ToString())
        );

        Assert.True(await IsAuthorizedAsync(principal, ScribePermissions.TemplatesRead));
    }

    [Fact]
    public async Task Token_with_perm_claim_TemplatesRead_is_still_rejected_for_TemplatesWrite()
    {
        // El claim "perm" es específico del permiso — tener templates.read no habilita
        // templates.write (Create/AddDraftVersion/PublishVersion).
        var principal = PrincipalWith(new Claim("perm", ScribePermissions.TemplatesRead));

        Assert.False(await IsAuthorizedAsync(principal, ScribePermissions.TemplatesWrite));
    }

    [Fact]
    public async Task Token_with_perm_claim_TemplatesWrite_is_authorized_for_TemplatesWrite()
    {
        var principal = PrincipalWith(new Claim("perm", ScribePermissions.TemplatesWrite));

        Assert.True(await IsAuthorizedAsync(principal, ScribePermissions.TemplatesWrite));
    }

    [Fact]
    public async Task TenantAdmin_role_without_perm_claim_is_rejected_for_TemplatesWrite()
    {
        // Auditoría de seguridad post-hardening: TenantAdmin YA NO pasa por rol solo — Auth
        // computa su set completo de claims "perm" al login (PermissionCatalog.SystemRoleDefaults,
        // que sigue incluyendo templates.write por defecto), así que el rol solo, sin el claim
        // real, ahora se rechaza (antes esto pasaba por el bypass de ClaimsPrincipalExtensions.
        // HasPermission, retirado tras encontrar que dejaba pasar signature.constraints.manage
        // — un permiso 100% PlatformOnly — a cualquier TenantAdmin).
        var principal = PrincipalWith(new Claim(ClaimTypes.Role, "TenantAdmin"));

        Assert.False(await IsAuthorizedAsync(principal, ScribePermissions.TemplatesWrite));
    }

    [Fact]
    public async Task TenantAdmin_role_with_perm_claim_is_authorized_for_TemplatesWrite()
    {
        // El claim sí lo tiene en la práctica: Auth se lo otorga por defecto vía
        // PermissionCatalog.SystemRoleDefaults al emitir el JWT — este test simula ese JWT real.
        var principal = PrincipalWith(
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("perm", ScribePermissions.TemplatesWrite)
        );

        Assert.True(await IsAuthorizedAsync(principal, ScribePermissions.TemplatesWrite));
    }

    [Fact]
    public async Task PlatformAdmin_role_is_authorized_for_TemplatesWrite_without_any_perm_claim()
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.Role, "PlatformAdmin"));

        Assert.True(await IsAuthorizedAsync(principal, ScribePermissions.TemplatesWrite));
    }

    [Fact]
    public async Task TenantEmployee_role_without_perm_claim_is_rejected_for_TemplatesWrite()
    {
        // A diferencia de TenantAdmin, el rol por sí solo no basta — un empleado necesita el
        // claim "perm" real (que hoy Auth solo otorga si el token trae templates.write asignado;
        // por defecto SystemEmployee NO lo tiene, solo templates.read — ver
        // PermissionCatalog.SystemRoleDefaults).
        var principal = PrincipalWith(new Claim(ClaimTypes.Role, "TenantEmployee"));

        Assert.False(await IsAuthorizedAsync(principal, ScribePermissions.TemplatesWrite));
    }

    [Fact]
    public async Task Service_token_with_perm_claim_ScribeRender_is_authorized_for_Render()
    {
        // Caso M2M real (Fase 10.5): un token de servicio no tiene ClaimTypes.Role (ver
        // JwtTokenGenerator.GenerateServiceToken — solo agrega actor_type=Service + los "perm"
        // configurados en ServiceAuth:Clients), así que scribe.render SOLO se satisface por el
        // claim "perm" explícito, nunca por un bypass de rol. Antes de la Fase 10.5, ni la
        // migración sembraba la fila ni ServiceAuth:Clients del notification-worker incluía
        // scribe.render en su Permissions — este caso habría fallado (403) de punta a punta.
        var principal = PrincipalWith(
            new Claim("perm", ScribePermissions.Render),
            new Claim("actor_type", "Service"),
            new Claim("client_id", "notification-worker")
        );

        Assert.True(await IsAuthorizedAsync(principal, ScribePermissions.Render));
    }

    [Fact]
    public async Task Service_token_without_perm_claim_is_rejected_for_Render()
    {
        // Mismo shape de token de servicio que arriba, pero sin scribe.render en su Permissions
        // configurados — no hay bypass de rol posible para un actor_type=Service, así que esto
        // debe rechazarse limpio (403 real), no colarse por ningún otro mecanismo.
        var principal = PrincipalWith(
            new Claim("actor_type", "Service"),
            new Claim("client_id", "notification-worker")
        );

        Assert.False(await IsAuthorizedAsync(principal, ScribePermissions.Render));
    }
}
