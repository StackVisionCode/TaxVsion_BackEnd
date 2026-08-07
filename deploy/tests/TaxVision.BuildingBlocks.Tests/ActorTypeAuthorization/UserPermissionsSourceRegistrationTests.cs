using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization;

/// <summary>
/// H-05 — el modo <c>Jwt</c> quedó inoperante para humanos cuando la Fase 7.5.10 sacó el claim
/// <c>perm</c> del token. Estos tests fijan que un servicio con endpoints <c>[HasPermission]</c>
/// reviente al arrancar en vez de responder 403 en silencio.
/// </summary>
public sealed class UserPermissionsSourceRegistrationTests
{
    // Este mismo assembly tiene un endpoint gateado (ver GatedEndpointsProbe, abajo).
    private static readonly Assembly WithGatedEndpoints = typeof(UserPermissionsSourceRegistrationTests).Assembly;

    // BuildingBlocks core no tiene controllers ni [HasPermission] — el atributo vive en .Web.
    private static readonly Assembly WithoutGatedEndpoints = typeof(ActorType).Assembly;

    [Theory]
    [InlineData(null)]
    [InlineData("Jwt")]
    [InlineData("projection")] // La comparación es ordinal: el casing tiene que coincidir.
    public void Revienta_si_hay_endpoints_gateados_y_el_modo_no_es_Projection(string? mode)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddUserPermissionsSource(Configuration(mode), WithGatedEndpoints)
        );

        Assert.Contains("Authorization:PermissionsSource", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(GatedEndpointsProbe), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void En_modo_Projection_registra_la_proyeccion_aunque_haya_endpoints_gateados()
    {
        var services = new ServiceCollection().AddUserPermissionsSource(
            Configuration("Projection"),
            WithGatedEndpoints
        );

        Assert.Equal(typeof(ProjectionPermissionsSource), ImplementationOf<IUserPermissionsSource>(services));
    }

    [Fact]
    public void Un_servicio_que_no_gatea_nada_puede_seguir_arrancando_en_modo_Jwt()
    {
        var services = new ServiceCollection().AddUserPermissionsSource(Configuration("Jwt"), WithoutGatedEndpoints);

        Assert.Equal(typeof(JwtEmbeddedPermissionsSource), ImplementationOf<IUserPermissionsSource>(services));
    }

    private static IConfiguration Configuration(string? mode) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                mode is null ? [] : new Dictionary<string, string?> { ["Authorization:PermissionsSource"] = mode }
            )
            .Build();

    private static Type? ImplementationOf<TService>(IServiceCollection services) =>
        services.Single(descriptor => descriptor.ServiceType == typeof(TService)).ImplementationType;

    /// <summary>Señuelo: existe solo para que este assembly cuente como "gatea por permiso".</summary>
    private sealed class GatedEndpointsProbe
    {
        [HasPermission("taxvision.probe")]
        public void Gated() { }
    }
}
