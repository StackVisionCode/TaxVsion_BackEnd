using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Postmaster.Infrastructure.Persistence;

/// <summary>
/// Factory de tiempo de diseño para dotnet-ef: evita levantar el host completo
/// (RabbitMQ/JWT) al crear o aplicar migraciones. La cadena de conexión se toma,
/// en orden, de: --connection, la variable ConnectionStrings__Default, o un
/// fallback local de desarrollo.
/// </summary>
public sealed class PostmasterDbContextFactory : IDesignTimeDbContextFactory<PostmasterDbContext>
{
    public PostmasterDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Postmaster;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<PostmasterDbContext>().UseSqlServer(connectionString).Options;

        return new PostmasterDbContext(options);
    }
}
