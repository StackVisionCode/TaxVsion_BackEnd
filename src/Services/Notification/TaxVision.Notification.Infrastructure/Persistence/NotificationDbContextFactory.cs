using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Notification.Infrastructure.Persistence;

/// <summary>
/// Factory de tiempo de diseño para dotnet-ef: evita levantar el host completo
/// (RabbitMQ/JWT) al crear o aplicar migraciones. La cadena de conexión se toma,
/// en orden, de: --connection, la variable ConnectionStrings__Default, o un
/// fallback local de desarrollo.
/// </summary>
public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVisionNotification;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<NotificationDbContext>().UseSqlServer(connectionString).Options;

        return new NotificationDbContext(options);
    }
}
