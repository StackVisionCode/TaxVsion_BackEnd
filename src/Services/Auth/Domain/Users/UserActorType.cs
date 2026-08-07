namespace TaxVision.Auth.Domain.Users;

/// <summary>
/// El actor humano dentro del bounded context de Auth. Es el lado emisor del claim
/// <c>actor_type</c> que los otros 17 microservicios leen tipado como
/// <c>BuildingBlocks.ActorTypeAuthorization.ActorType</c> (que además tiene <c>Service</c> para las
/// llamadas M2M, que no son un usuario).
///
/// <para>
/// H-08 — el orden de los valores es <b>idéntico</b> al de <c>ActorType</c> a propósito:
/// <c>TenantAdmin</c> y <c>CustomerPortal</c> estaban invertidos, así que los dos enums coincidían
/// por nombre pero no por ordinal. Hoy no rompe nada porque el claim viaja como string y la
/// persistencia usa <c>HasConversion&lt;string&gt;()</c>, pero cualquier transporte numérico futuro
/// habría convertido un <c>CustomerPortal</c> en <c>TenantAdmin</c> en silencio.
/// <c>ActorTypeParityTests</c> falla si vuelven a divergir.
/// </para>
/// </summary>
public enum UserActorType
{
    TenantEmployee,
    TenantAdmin,
    CustomerPortal,
    PlatformAdmin,
}

public static class UserActorRoles
{
    public static string For(UserActorType actorType) =>
        actorType switch
        {
            UserActorType.TenantEmployee => "TenantEmployee",
            UserActorType.TenantAdmin => "TenantAdmin",
            UserActorType.CustomerPortal => "CustomerPortal",
            UserActorType.PlatformAdmin => "PlatformAdmin",
            _ => throw new ArgumentOutOfRangeException(nameof(actorType), actorType, null),
        };
}
