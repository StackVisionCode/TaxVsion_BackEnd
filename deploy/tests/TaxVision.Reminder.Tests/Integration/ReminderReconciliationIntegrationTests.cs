using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;
using TaxVision.Reminder.Infrastructure.Persistence;
using TaxVision.Reminder.Infrastructure.Persistence.Repositories;

namespace TaxVision.Reminder.Tests.Integration;

/// <summary>
/// REM-2 contra el SQL Server real: la consulta de la reconciliación trae los <c>Snoozed</c>. Solo filas
/// en <c>Reminders</c> (sin triggers de Quartz), con tenant y usuario sintéticos, y limpieza por TenantId.
/// </summary>
public sealed class ReminderReconciliationIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Reminders.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task SnoozedWithoutTrigger_IsListedForRepair()
    {
        var nowUtc = DateTime.UtcNow;
        var reminder = ReminderAggregate
            .Create(
                _tenantId,
                Guid.NewGuid(),
                ReminderSubject.Create("Reconciliation probe", null).Value,
                ReminderTarget.Create(ReminderCategory.General, null).Value,
                ReminderSchedule.Absolute(nowUtc.AddMinutes(1), nowUtc).Value,
                ReminderTimeZone.Create("UTC").Value,
                RequestKey.Create($"reconciliation-probe-{Guid.NewGuid():N}").Value,
                nowUtc
            )
            .Value;
        Assert.True(reminder.FireOrMiss(nowUtc.AddMinutes(1), TimeSpan.FromMinutes(5)).IsSuccess);
        Assert.True(reminder.Snooze(TimeSpan.FromHours(2), nowUtc.AddMinutes(1)).IsSuccess);
        Assert.Equal(ReminderStatus.Snoozed, reminder.Status);

        await using (var db = NewContext())
        {
            db.Reminders.Add(reminder);
            await db.SaveChangesAsync();
        }

        await using var readDb = NewContext();
        var pending = await new ReminderRepository(readDb).ListPendingWithinHorizonAsync(nowUtc.AddHours(24));

        Assert.Contains(pending, r => r.Id == reminder.Id);
    }

    private static ReminderDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<ReminderDbContext>()
                .UseSqlServer(ReminderTestDatabase.ConnectionString)
                .Options,
            new NoTenantContext()
        );

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}

/// <summary>Cadena de la base local para las T-INT: variable de entorno o user-secrets del Api de Reminder.</summary>
internal static class ReminderTestDatabase
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("REMINDER_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("7e9e9b63-4868-4cc3-8876-b96a161ee297")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set REMINDER_DB_CONNECTION or the Reminder Api user-secrets to run this test."
        );
}
