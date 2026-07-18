using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Api.Common;

namespace TaxVision.PaymentClient.Api.Authorization;

/// <summary>
/// Autorización por permiso: <c>[HasPermission(PaymentClientPermissions.PaymentCharge)]</c>.
/// Mismo mecanismo que PaymentApp, Signature, Notification y Auth. Los administradores pasan
/// siempre; el resto necesita el claim "perm" correspondiente.
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
