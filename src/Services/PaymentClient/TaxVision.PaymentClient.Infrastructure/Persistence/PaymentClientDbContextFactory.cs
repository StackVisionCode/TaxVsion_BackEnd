using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.PaymentClient.Infrastructure.Persistence;

/// <summary>
/// Factory de tiempo de diseño para dotnet-ef: evita levantar el host completo
/// (RabbitMQ/JWT) al crear o aplicar migraciones.
/// </summary>
public sealed class PaymentClientDbContextFactory : IDesignTimeDbContextFactory<PaymentClientDbContext>
{
    public PaymentClientDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVisionPaymentClient;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<PaymentClientDbContext>().UseSqlServer(connectionString).Options;

        return new PaymentClientDbContext(options);
    }
}
