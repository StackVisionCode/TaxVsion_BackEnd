using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Connectors.Infrastructure.Persistence;

/// <summary>Design-time factory para <c>dotnet ef</c> — evita levantar RabbitMQ/JWT solo para migrar.</summary>
public sealed class ConnectorsDbContextFactory : IDesignTimeDbContextFactory<ConnectorsDbContext>
{
    public ConnectorsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Connectors;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<ConnectorsDbContext>().UseSqlServer(connectionString).Options;

        return new ConnectorsDbContext(options);
    }
}
