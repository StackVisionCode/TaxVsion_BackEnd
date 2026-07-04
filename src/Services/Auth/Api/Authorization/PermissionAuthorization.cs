using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Api.Common;

namespace TaxVision.Auth.Api.Authorization;

/// <summary>
/// Autorización por permiso: [HasPermission(PermissionCatalog.UsersInvite)].
/// Los administradores (TenantAdmin/PlatformAdmin) pasan siempre; el resto
/// necesita el claim "perm" correspondiente en su JWT.
/// </summary>
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public HasPermissionAttribute(string permission)
        : base($"{PolicyPrefix}{permission}") { }
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var permission = policyName[HasPermissionAttribute.PolicyPrefix.Length..];
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.HasPermission(permission))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}
