using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;
using TaxVision.Reminder.Infrastructure.Persistence;
using TaxVision.Reminder.Infrastructure.Scheduling;

namespace TaxVision.Reminder.Tests.Scheduling;

/// <summary>
/// Va contra SQL Server real por la misma razón que <c>ReminderIdempotencyTests</c>, más una propia:
/// <c>ExecuteDeleteAsync</c> <b>no existe</b> en el proveedor InMemory, así que un test in-memory de
/// este job no compilaría siquiera contra el código que se quiere probar.
///
/// <para>
/// Lo que de verdad se verifica: que el filtro por <c>ResolvedAtUtc</c> discrimine, que los estados
/// no terminales sobrevivan, y que <c>RetentionMonths = 0</c> desactive el barrido. El
/// <c>IgnoreQueryFilters()</c> se prueba solo: el job corre sin tenant en contexto y sin él la
/// consulta devolvería 0 filas y las aserciones de borrado fallarían.
/// </para>
/// </summary>
public sealed class ReminderRetentionJobTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=TaxVision_Reminder;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static ReminderDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<ReminderDbContext>().UseSqlServer(ConnectionString).Options,
            new NoTenantContext()
        );

    private static ReminderRetentionJob CreateJob(int retentionMonths)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateContext());

        return new ReminderRetentionJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReminderSchedulingOptions { RetentionMonths = retentionMonths }),
            NullLogger<ReminderRetentionJob>.Instance
        );
    }

    private static ReminderAggregate NewReminder(Guid tenantId, DateTime createdAtUtc)
    {
        return ReminderAggregate
            .Create(
                tenantId,
                Guid.NewGuid(),
                ReminderSubject.Create("Retention probe", null).Value,
                ReminderTarget.Create(ReminderCategory.General, null).Value,
                ReminderSchedule.Absolute(createdAtUtc.AddDays(1), createdAtUtc).Value,
                ReminderTimeZone.Create("UTC").Value,
                RequestKey.Create($"retention-probe-{Guid.NewGuid():N}").Value,
                createdAtUtc
            )
            .Value;
    }

    private static ReminderAggregate Cancelled(Guid tenantId, DateTime createdAtUtc, DateTime resolvedAtUtc)
    {
        var reminder = NewReminder(tenantId, createdAtUtc);
        Assert.True(reminder.Cancel(ReminderCancellationReasons.UserRequest, resolvedAtUtc).IsSuccess);
        return reminder;
    }

    [Fact]
    public async Task Purges_terminal_reminders_older_than_the_retention_window_and_leaves_the_rest()
    {
        var tenantId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        // Creado hace tres años pero cancelado ayer: es reciente para soporte y NO se debe borrar.
        // Éste es el caso que un filtro por CreatedAtUtc perdería en silencio.
        var recentlyResolved = Cancelled(tenantId, nowUtc.AddYears(-3), nowUtc.AddDays(-1));
        var longResolved = Cancelled(tenantId, nowUtc.AddYears(-3), nowUtc.AddMonths(-18));

        // Programado: no terminal, ResolvedAtUtc null. Sobrevive pase lo que pase.
        var stillScheduled = NewReminder(tenantId, nowUtc.AddYears(-3));

        try
        {
            await using (var seedDb = CreateContext())
            {
                seedDb.Reminders.AddRange(recentlyResolved, longResolved, stillScheduled);
                await seedDb.SaveChangesAsync();
            }

            await CreateJob(retentionMonths: 12).PurgeAsync(CancellationToken.None);

            await using var verifyDb = CreateContext();
            var survivors = await verifyDb
                .Reminders.IgnoreQueryFilters()
                .Where(r => r.TenantId == tenantId)
                .Select(r => r.Id)
                .ToListAsync();

            Assert.DoesNotContain(longResolved.Id, survivors);
            Assert.Contains(recentlyResolved.Id, survivors);
            Assert.Contains(stillScheduled.Id, survivors);
        }
        finally
        {
            await using var cleanupDb = CreateContext();
            await cleanupDb.Reminders.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Retention_of_zero_months_disables_the_purge_entirely()
    {
        var tenantId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;
        var ancient = Cancelled(tenantId, nowUtc.AddYears(-5), nowUtc.AddYears(-5));

        try
        {
            await using (var seedDb = CreateContext())
            {
                seedDb.Reminders.Add(ancient);
                await seedDb.SaveChangesAsync();
            }

            await CreateJob(retentionMonths: 0).PurgeAsync(CancellationToken.None);

            await using var verifyDb = CreateContext();
            Assert.Equal(1, await verifyDb.Reminders.IgnoreQueryFilters().CountAsync(r => r.TenantId == tenantId));
        }
        finally
        {
            await using var cleanupDb = CreateContext();
            await cleanupDb.Reminders.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).ExecuteDeleteAsync();
        }
    }
}
