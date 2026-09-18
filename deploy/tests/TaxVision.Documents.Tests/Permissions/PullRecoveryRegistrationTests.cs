using BuildingBlocks.Permissions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Documents.Infrastructure;
using Xunit;

namespace TaxVision.Documents.Tests.Permissions;

/// <summary>
/// Regresión Opción B (recuperación pull bajo demanda): Documents DEBE registrar el snapshot client y
/// el projection writer. Sin ambos, <c>ProjectionPermissionsSource</c> trata un miss local de proyección
/// como definitivo y un usuario cuyo <c>UserRolesChangedIntegrationEvent</c> nunca llegó a este servicio
/// (típicamente porque Documents estaba caído durante el broadcast de Auth) queda 403 permanente — el caso
/// real de un Tenant Admin con el permiso en Auth pero 403 en <c>GET /documents/branding</c>.
/// </summary>
public sealed class PullRecoveryRegistrationTests
{
    [Fact]
    public void AddDocumentsInfrastructure_registra_el_snapshot_client_y_el_projection_writer()
    {
        var services = new ServiceCollection().AddDocumentsInfrastructure(Configuration());

        Assert.Contains(services, d => d.ServiceType == typeof(IPermissionsSnapshotClient));
        Assert.Contains(services, d => d.ServiceType == typeof(IUserPermissionsProjectionWriter));
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    // AddDocumentsInfrastructure solo LEE la cadena en el registro (AddDbContext es perezoso):
                    // no abre conexión, así que un placeholder alcanza para inspeccionar los descriptores.
                    ["ConnectionStrings:Default"] = "Server=localhost;Database=x;TrustServerCertificate=true;",
                }
            )
            .Build();
}
