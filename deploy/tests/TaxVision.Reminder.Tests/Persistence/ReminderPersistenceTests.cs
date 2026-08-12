using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;
using TaxVision.Reminder.Infrastructure.Persistence;
using TaxVision.Reminder.Infrastructure.Persistence.Repositories;

namespace TaxVision.Reminder.Tests.Persistence;

/// <summary>
/// Checkpoint 2 de <c>03_Plan_De_Fases.md</c>, la parte que sí se puede cubrir con el proveedor
/// InMemory: mapeo de los 5 VOs (3 owned + 2 por HasConversion) y aislamiento por tenant.
///
/// <para>
/// La idempotencia del índice único va aparte, en <see cref="ReminderIdempotencyTests"/>: InMemory
/// <b>no aplica índices únicos</b>, así que un test de duplicados aquí pasaría siempre y no
/// probaría nada.
/// </para>
/// </summary>
public sealed class ReminderPersistenceTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static ReminderDbContext CreateContext(string databaseName, ITenantContext tenantContext) =>
        new(new DbContextOptionsBuilder<ReminderDbContext>().UseInMemoryDatabase(databaseName).Options, tenantContext);

    private static ReminderAggregate NewReminder(
        Guid tenantId,
        Guid userId,
        string requestKey = "req-1",
        ReminderCategory category = ReminderCategory.Task,
        Guid? targetId = null
    )
    {
        var nowUtc = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        return ReminderAggregate
            .Create(
                tenantId,
                userId,
                ReminderSubject.Create("Revisar la declaración", "Antes del cierre del trimestre.").Value,
                ReminderTarget.Create(category, targetId ?? Guid.NewGuid()).Value,
                ReminderSchedule.Anchored(nowUtc.AddDays(3), 60, nowUtc).Value,
                ReminderTimeZone.Create("America/New_York").Value,
                RequestKey.Create(requestKey).Value,
                nowUtc
            )
            .Value;
    }

    [Fact]
    public async Task Reminder_with_all_five_value_objects_persists_and_reloads_correctly()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);
        Guid reminderId;

        await using (var db = CreateContext(databaseName, tenantContext))
        {
            var reminder = NewReminder(tenantId, Guid.NewGuid(), targetId: targetId);
            reminderId = reminder.Id;
            db.Reminders.Add(reminder);
            await db.SaveChangesAsync();
        }

        await using var reloadDb = CreateContext(databaseName, tenantContext);
        var reloaded = await reloadDb.Reminders.SingleAsync(r => r.Id == reminderId);

        Assert.Equal("Revisar la declaración", reloaded.Subject.Title);
        Assert.Equal("Antes del cierre del trimestre.", reloaded.Subject.Body);
        Assert.Equal(ReminderCategory.Task, reloaded.Target.Category);
        Assert.Equal(targetId, reloaded.Target.TargetId);
        Assert.Equal(new DateTime(2026, 8, 14, 11, 0, 0, DateTimeKind.Utc), reloaded.Schedule.FireAtUtc);
        Assert.Equal(new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc), reloaded.Schedule.AnchorAtUtc);
        Assert.Equal(60, reloaded.Schedule.LeadMinutes);

        // Los dos VOs mapeados con HasConversion: el round-trip pasa por VO.Create(...).Value, así
        // que si el mapeo estuviera mal el reload reventaría en vez de devolver un valor incorrecto.
        Assert.Equal("America/New_York", reloaded.TimeZone.Value);
        Assert.Equal("req-1", reloaded.RequestKey.Value);
        Assert.Equal(ReminderStatus.Scheduled, reloaded.Status);
    }

    [Fact]
    public async Task Global_tenant_filter_hides_reminders_belonging_to_a_different_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();

        tenantContext.SetTenant(ownerTenantId);
        await using (var writeDb = CreateContext(databaseName, tenantContext))
        {
            writeDb.Reminders.Add(NewReminder(ownerTenantId, Guid.NewGuid()));
            await writeDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(otherTenantId);
        await using var readDb = CreateContext(databaseName, tenantContext);

        Assert.Empty(await readDb.Reminders.ToListAsync());
    }

    [Fact]
    public async Task Cross_tenant_job_query_sees_every_tenant_because_it_ignores_query_filters()
    {
        var databaseName = Guid.NewGuid().ToString();
        var firstTenantId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(firstTenantId);

        await using var db = CreateContext(databaseName, tenantContext);
        db.Reminders.Add(NewReminder(firstTenantId, Guid.NewGuid(), "req-a"));
        db.Reminders.Add(NewReminder(secondTenantId, Guid.NewGuid(), "req-b"));
        await db.SaveChangesAsync();

        var repository = new ReminderRepository(db);

        // El job de la Fase 5 corre sin tenant en contexto. Sin IgnoreQueryFilters() esto
        // devolvería solo el del tenant actual (o 0 filas fuera de un request) y el job parecería
        // sano sin hacer nada — el bug que este test existe para impedir.
        var due = await repository.ListScheduledWithinHorizonAsync(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, due.Count);
        Assert.Contains(due, r => r.TenantId == firstTenantId);
        Assert.Contains(due, r => r.TenantId == secondTenantId);
    }

    [Fact]
    public async Task GetByIdAsync_returns_NotFound_error_instead_of_null_when_the_reminder_does_not_exist()
    {
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(Guid.NewGuid());
        await using var db = CreateContext(Guid.NewGuid().ToString(), tenantContext);

        var result = await new ReminderRepository(db).GetByIdAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ReminderErrors.NotFound.Code, result.Error.Code);
    }
}
