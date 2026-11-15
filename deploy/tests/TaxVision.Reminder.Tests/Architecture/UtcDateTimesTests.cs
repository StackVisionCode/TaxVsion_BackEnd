using BuildingBlocks.EfCore;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Reminder.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Reminder.Tests.Architecture;

/// <summary>Cada <c>DateTime</c> del modelo se lee como UTC, así la respuesta JSON lleva "Z".</summary>
public sealed class UtcDateTimesTests
{
    [Fact]
    public void Every_datetime_column_is_read_as_utc()
    {
        using var db = new ReminderDbContext(
            new DbContextOptionsBuilder<ReminderDbContext>().UseSqlServer("Server=unused;Database=unused").Options,
            new NoTenant()
        );

        Assert.Empty(UtcDateTimeConventions.DateTimesNotReadAsUtc(db.Model));
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
