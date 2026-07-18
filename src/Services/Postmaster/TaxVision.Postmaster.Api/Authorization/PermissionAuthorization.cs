using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TaxVision.Postmaster.Api.Common;

namespace TaxVision.Postmaster.Api.Authorization;

/// <summary>
/// Autorización por permiso: <c>[HasPermission(PostmasterPermissions.MessagesRead)]</c>.
/// Mismo mecanismo que Signature/Notification/Customer. Los administradores pasan siempre; el
/// resto necesita el claim "perm" correspondiente.
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
