using System.Text.Json;
using BuildingBlocks.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.EfCore;

public sealed class UtcDateTimeConventionsTests
{
    private sealed class Row
    {
        public Guid Id { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ClosedAtUtc { get; set; }
        public DateTime LegacyCivilDay { get; set; }
        public DateTime Converted { get; set; }
    }

    private class RowsDb(DbContextOptions options, bool utc) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Row>(b =>
            {
                b.Property(x => x.LegacyCivilDay).IsCivilDateTime();
                b.Property(x => x.Converted)
                    .HasConversion(new ValueConverter<DateTime, long>(v => v.Ticks, v => new DateTime(v)));
            });
            if (utc)
                modelBuilder.UseUtcDateTimes();
        }
    }

    // EF guarda el modelo por tipo de contexto: sin la convención va en otro tipo.
    private sealed class PlainRowsDb(DbContextOptions options) : RowsDb(options, utc: false);

    private static RowsDb Db(string name, bool utc = true)
    {
        var options = new DbContextOptionsBuilder().UseInMemoryDatabase(name).Options;
        return utc ? new RowsDb(options, utc: true) : new PlainRowsDb(options);
    }

    [Fact]
    public async Task Instants_are_read_back_as_utc_and_serialize_with_z()
    {
        var name = Guid.NewGuid().ToString();
        var id = Guid.NewGuid();
        await using (var write = Db(name))
        {
            write.Rows.Add(
                new Row
                {
                    Id = id,
                    CreatedAtUtc = new DateTime(2026, 11, 15, 15, 0, 0, DateTimeKind.Unspecified),
                    ClosedAtUtc = new DateTime(2026, 11, 16, 1, 0, 0, DateTimeKind.Unspecified),
                    LegacyCivilDay = new DateTime(2026, 11, 15),
                }
            );
            await write.SaveChangesAsync();
        }

        await using var read = Db(name);
        var row = await read.Rows.SingleAsync(r => r.Id == id);

        Assert.Equal(DateTimeKind.Utc, row.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, row.ClosedAtUtc!.Value.Kind);
        Assert.Equal("\"2026-11-15T15:00:00Z\"", JsonSerializer.Serialize(row.CreatedAtUtc));
        // La fecha civil marcada queda como estaba.
        Assert.Equal(DateTimeKind.Unspecified, row.LegacyCivilDay.Kind);
    }

    [Fact]
    public void A_property_with_its_own_converter_is_left_alone()
    {
        using var db = Db(Guid.NewGuid().ToString());
        var converter = db.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.Converted))!.GetValueConverter();
        Assert.Equal(typeof(long), converter!.ProviderClrType);
    }

    [Fact]
    public void The_fitness_check_lists_datetimes_that_are_not_read_as_utc()
    {
        using var without = Db(Guid.NewGuid().ToString(), utc: false);
        using var with = Db(Guid.NewGuid().ToString());

        Assert.Equal(
            ["Row.ClosedAtUtc", "Row.CreatedAtUtc"],
            UtcDateTimeConventions.DateTimesNotReadAsUtc(without.Model)
        );
        Assert.Empty(UtcDateTimeConventions.DateTimesNotReadAsUtc(with.Model));
    }
}
