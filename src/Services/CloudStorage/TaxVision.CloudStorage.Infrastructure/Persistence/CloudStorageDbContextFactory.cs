using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.CloudStorage.Infrastructure.Persistence;

public sealed class CloudStorageDbContextFactory : IDesignTimeDbContextFactory<CloudStorageDbContext>
{
    public CloudStorageDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CLOUDSTORAGE_DB_CONNECTION")
            ?? "Server=localhost;Database=TaxVision_CloudStorage;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<CloudStorageDbContext>().UseSqlServer(connectionString).Options;
        return new CloudStorageDbContext(options);
    }
}
