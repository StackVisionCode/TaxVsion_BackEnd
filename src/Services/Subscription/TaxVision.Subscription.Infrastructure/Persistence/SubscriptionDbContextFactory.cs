using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Subscription.Infrastructure.Persistence;

/// <summary>
/// Usado exclusivamente por EF Core Tools en design-time (migrations, scaffolding).
/// Lee la connection string desde el archivo .env en la raíz del repositorio.
/// Reemplaza host.docker.internal → localhost para que funcione fuera de Docker.
/// </summary>
public sealed class SubscriptionDbContextFactory : IDesignTimeDbContextFactory<SubscriptionDbContext>
{
    public SubscriptionDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString();

        var optionsBuilder = new DbContextOptionsBuilder<SubscriptionDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new SubscriptionDbContext(optionsBuilder.Options, new DesignTimeTenantContext());
    }

    private static string ResolveConnectionString()
    {
        var envVars = LoadDotEnv();

        if (envVars.TryGetValue("SUBSCRIPTION_DB_CONNECTION", out var conn) && !string.IsNullOrWhiteSpace(conn))
            return conn.Replace("host.docker.internal", "localhost", StringComparison.OrdinalIgnoreCase);

        // Fallback: SQLEXPRESS local sin credenciales
        return "Server=localhost,1433;Database=TaxVision_Subscriptions;User Id=sa;Password=TaxVision@Dev1;TrustServerCertificate=true";
    }

    /// <summary>
    /// Sube por directorios desde el directorio actual hasta encontrar el .env del repo.
    /// </summary>
    private static Dictionary<string, string> LoadDotEnv()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var envPath = Path.Combine(dir.FullName, ".env");
            if (File.Exists(envPath))
                return ParseEnvFile(envPath);
            dir = dir.Parent;
        }
        return [];
    }

    private static Dictionary<string, string> ParseEnvFile(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
            var idx = trimmed.IndexOf('=');
            if (idx < 0) continue;
            var key = trimmed[..idx].Trim();
            var val = trimmed[(idx + 1)..].Trim();
            result[key] = val;
        }
        return result;
    }
}

file sealed class DesignTimeTenantContext : ITenantContext
{
    public Guid TenantId => Guid.Empty;
    public bool HasTenant => false;
}
