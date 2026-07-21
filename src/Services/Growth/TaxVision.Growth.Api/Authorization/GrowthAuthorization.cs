using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TaxVision.Growth.Api.Common;

namespace TaxVision.Growth.Api.Authorization;

public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public HasPermissionAttribute(string permission)
        : base($"{PolicyPrefix}{permission}") { }
}

public sealed class HasServiceScopeAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "service-scope:";

    public HasServiceScopeAttribute(string scope)
        : base($"{PolicyPrefix}{scope}") { }
}

public static class GrowthAuthentication
{
    public const string Audience = "taxvision-growth";
}

/// <summary>
/// Dynamic permission and service-scope policies. Roles never bypass either policy:
/// administrators and services must receive the explicit claim from Auth.
/// </summary>
public sealed class GrowthAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
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

        if (policyName.StartsWith(HasServiceScopeAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var scope = policyName[HasServiceScopeAttribute.PolicyPrefix.Length..];
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim("actor_type", "Service")
                .RequireAssertion(context =>
                    context.User.HasAudience(GrowthAuthentication.Audience) && context.User.HasScope(scope)
                )
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}
