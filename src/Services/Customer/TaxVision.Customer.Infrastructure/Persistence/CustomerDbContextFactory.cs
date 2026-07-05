using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Customer.Infrastructure.Persistence;

/// <summary>
/// Factory de tiempo de diseño para dotnet-ef: evita levantar el host completo
/// (JWT/RabbitMQ/user-secrets) al crear o aplicar migraciones. La cadena de
/// conexión se toma de --connection, de la variable ConnectionStrings__Default,
/// o de un fallback local de desarrollo.
/// </summary>
public sealed class CustomerDbContextFactory : IDesignTimeDbContextFactory<CustomerDbContext>
{
    public CustomerDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Customers;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<CustomerDbContext>().UseSqlServer(connectionString).Options;

        return new CustomerDbContext(options);
    }
}
