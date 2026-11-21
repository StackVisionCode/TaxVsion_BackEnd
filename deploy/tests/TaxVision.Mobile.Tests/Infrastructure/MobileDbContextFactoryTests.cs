using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Infrastructure.Persistence;

namespace TaxVision.Mobile.Tests.Infrastructure;

/// <summary>
/// La factory de <c>dotnet ef</c> solo mira <c>ConnectionStrings__Default</c> o la base local: ni la variable del
/// <c>.env</c> de producción ni los user-secrets deciden a qué base apunta una migración.
/// </summary>
public sealed class MobileDbContextFactoryTests
{
    [Fact]
    public void Design_time_connection_is_the_explicit_variable_or_the_local_database()
    {
        // Solo lee el entorno: cambiarlo aquí afectaría a las pruebas de integración en paralelo.
        var expected = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? MobileDbContextFactory.LocalConnection
        );

        using var db = new MobileDbContextFactory().CreateDbContext([]);

        // Sin Assert.Equal: un fallo no debe imprimir una cadena de conexión.
        var actual = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        Assert.True(
            actual.DataSource == expected.DataSource && actual.InitialCatalog == expected.InitialCatalog,
            "The design-time factory points to a database other than the expected one."
        );
    }

    [Fact]
    public void Local_database_is_localhost() =>
        Assert.StartsWith("Server=localhost,1433;Database=TaxVision_Mobile;", MobileDbContextFactory.LocalConnection);
}
