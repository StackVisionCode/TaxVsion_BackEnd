using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Correspondence.Infrastructure.Persistence;

/// <summary>Design-time factory para <c>dotnet ef</c> — evita levantar RabbitMQ/JWT solo para migrar.</summary>
public sealed class CorrespondenceDbContextFactory : IDesignTimeDbContextFactory<CorrespondenceDbContext>
{
    public CorrespondenceDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Correspondence;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<CorrespondenceDbContext>().UseSqlServer(connectionString).Options;

        return new CorrespondenceDbContext(options);
    }
}
