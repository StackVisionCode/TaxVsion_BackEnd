using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// El borrado con token de servicio pasa por el pipeline real (actor, permiso y handler) sin tocar datos: se
/// borra un id que no existe. Si el actor <c>Service</c> no está admitido, el filtro responde 403 antes de
/// llegar al handler; admitido, el handler responde 404.
/// </summary>
public sealed class ServiceFileDeletionIntegrationTests(CloudStorageApiFactory factory)
    : IClassFixture<CloudStorageApiFactory>
{
    [Fact]
    public async Task A_service_with_the_delete_permission_reaches_the_handler()
    {
        var response = await DeleteUnknownFileAsync(["cloudstorage.file.delete"]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_service_without_the_delete_permission_is_still_rejected()
    {
        var response = await DeleteUnknownFileAsync(["cloudstorage.file.download"]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpResponseMessage> DeleteUnknownFileAsync(string[] permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            MintServiceToken(Guid.NewGuid(), permissions)
        );
        return await client.DeleteAsync($"/storage/files/{Guid.NewGuid()}");
    }

    // Igual que un token M2M de Auth: actor Service y permisos en claims perm.
    private string MintServiceToken(Guid tenantId, string[] permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("tenant_id", tenantId.ToString()),
            new("actor_type", "Service"),
            new("client_id", "customer-worker"),
        };
        claims.AddRange(permissions.Select(permission => new Claim("perm", permission)));

        var token = new JwtSecurityToken(
            issuer: factory.JwtIssuer,
            audience: factory.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.JwtSecret)),
                SecurityAlgorithms.HmacSha256
            )
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
