using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Intercepta las policies con prefijo <see cref="HasPermissionAttribute.PolicyPrefix"/> y las
/// construye contra <see cref="ClaimsPrincipalExtensions.HasPermission"/>. Cada microservicio
/// que use <see cref="HasPermissionAttribute"/> debe registrar esta clase como
/// <c>IAuthorizationPolicyProvider</c> en su <c>Program.cs</c> (Fase 3 — reemplaza a la copia
/// local que tenía cada uno).
/// </summary>
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
