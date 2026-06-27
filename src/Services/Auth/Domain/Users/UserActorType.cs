namespace TaxVision.Auth.Domain.Users;

public enum UserActorType
{
    TenantEmployee,
    CustomerPortal,
    TenantAdmin,
    PlatformAdmin
}

public static class UserActorRoles
{
    public static string For(UserActorType actorType) =>
        actorType switch
        {
            UserActorType.TenantEmployee => "TenantEmployee",
            UserActorType.CustomerPortal => "CustomerPortal",
            UserActorType.TenantAdmin => "TenantAdmin",
            UserActorType.PlatformAdmin => "PlatformAdmin",
            _ => throw new ArgumentOutOfRangeException(nameof(actorType), actorType, null)
        };
}
