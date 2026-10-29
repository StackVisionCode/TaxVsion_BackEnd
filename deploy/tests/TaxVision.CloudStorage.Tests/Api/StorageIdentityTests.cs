using System.Reflection;
using System.Security.Claims;
using TaxVision.CloudStorage.Api.Controllers;
using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Tests.Api;

/// <summary>Fotos de perfil F0 — el scope marca IsService solo con actor_type=Service (carpetas reservadas).</summary>
public sealed class StorageIdentityTests
{
    [Theory]
    [InlineData("Service", true)]
    [InlineData("TenantAdmin", false)]
    [InlineData("TenantEmployee", false)]
    [InlineData("CustomerPortal", false)]
    [InlineData("PlatformAdmin", false)]
    public void IsService_comes_only_from_a_service_token(string actorType, bool expected)
    {
        var user = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim("tenant_id", Guid.NewGuid().ToString()),
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim("actor_type", actorType),
                ],
                "test"
            )
        );

        var scope = TryGet(user);

        Assert.Equal(expected, scope.IsService);
    }

    // StorageIdentity es internal del Api: se invoca por reflexión, igual que otros tests de atributos.
    private static StorageActorScope TryGet(ClaimsPrincipal user)
    {
        var method = typeof(FilesController)
            .Assembly.GetType("TaxVision.CloudStorage.Api.Common.StorageIdentity", throwOnError: true)!
            .GetMethod("TryGet", BindingFlags.Public | BindingFlags.Static)!;
        object?[] args = [user, null, null, null];
        Assert.True((bool)method.Invoke(null, args)!);
        return (StorageActorScope)args[3]!;
    }
}
