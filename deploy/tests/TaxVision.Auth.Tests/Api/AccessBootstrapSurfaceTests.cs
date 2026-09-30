using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using TaxVision.Auth.Api.Controllers;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// §R.4.1 del plan — el bootstrap de acceso **nace consciente de la superficie**: un token del Account
/// del Landing no obtiene el bootstrap del CRM.
///
/// <para>
/// El mecanismo es la AUSENCIA de <c>[AllowSurface]</c>: <c>SurfaceAuthorizationFilter</c> es fail-closed
/// para cualquier token con claim <c>surface</c>, así que sin el atributo el filtro lo rechaza con
/// <c>Auth.SurfaceNotAllowed</c> antes de llegar al handler. Es una ausencia, y una ausencia se borra sin
/// que nada se rompa — de ahí este test: si alguien le agrega <c>[AllowSurface(Account)]</c> "para que
/// funcione también en el Landing", falla acá y no en producción.
/// </para>
/// </summary>
public sealed class AccessBootstrapSurfaceTests
{
    private static MethodInfo Endpoint(string name) =>
        typeof(AuthController).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"AuthController.{name} no existe.");

    [Fact]
    public void The_access_bootstrap_does_not_admit_the_account_surface()
    {
        var declared = Endpoint(nameof(AuthController.MyAccess)).GetCustomAttribute<AllowSurfaceAttribute>();

        Assert.Null(declared);
    }

    /// <summary>
    /// El contraste que da sentido al test de arriba: <c>GET /auth/me</c> sí admite la superficie Account
    /// (el Landing necesita saber quién está logueado). Lo que no admite es el bootstrap de acceso.
    /// </summary>
    [Fact]
    public void But_the_identity_endpoint_does()
    {
        var declared = Endpoint(nameof(AuthController.Me)).GetCustomAttribute<AllowSurfaceAttribute>();

        Assert.NotNull(declared);
        Assert.Contains(AccessSurface.Account, declared!.Surfaces);
    }

    [Fact]
    public void The_bootstrap_declares_which_actor_types_may_use_it()
    {
        var declared = Endpoint(nameof(AuthController.MyAccess)).GetCustomAttribute<AllowActorTypesAttribute>();

        Assert.NotNull(declared);
        Assert.Contains(ActorType.TenantEmployee, declared!.ActorTypes);
        Assert.Contains(ActorType.CustomerPortal, declared.ActorTypes);
    }
}
