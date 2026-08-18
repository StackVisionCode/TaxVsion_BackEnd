using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Calendar.Tests.Persistence;

/// <summary>
/// Estos tests van contra SQL Server real. InMemory no aplica índices únicos ni <c>rowversion</c>, y
/// —lo que más importa acá— no reproduce que <c>datetime2</c> vuelva con <c>Kind Unspecified</c>.
/// </summary>
public static class SqlServerFixture
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("CALENDAR_TEST_DB_CONNECTION")
        ?? "Server=localhost,1433;Database=TaxVision_Calendar;Trusted_Connection=True;"
            + "TrustServerCertificate=True;Encrypt=False";

    public static CalendarDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<CalendarDbContext>().UseSqlServer(ConnectionString).Options;

        return new CalendarDbContext(options, new FixedTenantContext(tenantId));
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;

        public bool HasTenant => tenantId != Guid.Empty;

        public void SetTenant(Guid value) { }
    }
}

/// <summary>
/// Serializa los tests de persistencia: comparten base y se borran las filas entre uno y otro. Sin
/// esto, xUnit los corre en paralelo y el borrado de uno se lleva las filas del que está corriendo.
/// </summary>
[CollectionDefinition(nameof(SqlServerCollection), DisableParallelization = true)]
public sealed class SqlServerCollection;
