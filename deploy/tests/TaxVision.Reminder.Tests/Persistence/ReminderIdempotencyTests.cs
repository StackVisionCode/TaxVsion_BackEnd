using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Reminder.Domain.ValueObjects;
using TaxVision.Reminder.Infrastructure.Persistence;

namespace TaxVision.Reminder.Tests.Persistence;

/// <summary>
/// Checkpoint 2 de <c>03_Plan_De_Fases.md</c>: «La idempotencia se prueba, no se asume».
///
/// <para>
/// Este es el único test del servicio que va contra SQL Server real, y tiene que serlo: el
/// proveedor InMemory <b>ignora los índices únicos</b>, así que la misma aserción sobre InMemory
/// pasaría con la restricción borrada. Es exactamente el modo de fallo que hay que evitar — un
/// test verde que no prueba nada.
/// </para>
/// <para>
/// El plan decía que el segundo insert tira <c>DbUpdateException</c>. Medido: tira
/// <see cref="ConflictException"/>, porque <see cref="ReminderDbContext.SaveChangesAsync"/>
/// traduce <c>SqlException</c> 2601/2627 antes de que la excepción salga del contexto. El plan
/// queda corregido; lo que importa es que la segunda escritura <b>no entra</b>.
/// </para>
/// </summary>
public sealed class ReminderIdempotencyTests
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
        new(new DbContextOptionsBuilder<ReminderDbContext>().UseSqlServer(ConnectionString).Options, new NoTenantContext());

    private static ReminderAggregate NewReminder(Guid tenantId, string requestKey)
    {
        var nowUtc = DateTime.UtcNow;
        return ReminderAggregate
            .Create(
                tenantId,
                Guid.NewGuid(),
                ReminderSubject.Create("Idempotency probe", null).Value,
                ReminderTarget.Create(ReminderCategory.General, null).Value,
                ReminderSchedule.Absolute(nowUtc.AddDays(1), nowUtc).Value,
                ReminderTimeZone.Create("UTC").Value,
                RequestKey.Create(requestKey).Value,
                nowUtc
            )
            .Value;
    }

    [Fact]
    public async Task Two_reminders_with_the_same_tenant_and_request_key_cannot_both_be_stored()
    {
        // Tenant sintético propio de esta ejecución: aísla el test de cualquier dato real de la
        // base local y hace que el cleanup del final no pueda borrar nada ajeno.
        var tenantId = Guid.NewGuid();
        var requestKey = $"idempotency-probe-{Guid.NewGuid():N}";

        try
        {
            await using (var firstDb = CreateContext())
            {
                firstDb.Reminders.Add(NewReminder(tenantId, requestKey));
                await firstDb.SaveChangesAsync();
            }

            await using var secondDb = CreateContext();
            secondDb.Reminders.Add(NewReminder(tenantId, requestKey));

            var conflict = await Assert.ThrowsAsync<ConflictException>(() => secondDb.SaveChangesAsync());
            Assert.Equal("Persistence.UniqueConstraint", conflict.Code);

            // Que la excepción salga no basta: hay que ver que la fila no quedó.
            await using var verifyDb = CreateContext();
            var stored = await verifyDb
                .Reminders.IgnoreQueryFilters()
                .CountAsync(r => r.TenantId == tenantId && r.RequestKey == RequestKey.Create(requestKey).Value);
            Assert.Equal(1, stored);
        }
        finally
        {
            await using var cleanupDb = CreateContext();
            await cleanupDb.Reminders.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task The_same_request_key_is_allowed_again_under_a_different_tenant()
    {
        var firstTenantId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        var requestKey = $"idempotency-probe-{Guid.NewGuid():N}";

        try
        {
            await using var db = CreateContext();
            db.Reminders.Add(NewReminder(firstTenantId, requestKey));
            db.Reminders.Add(NewReminder(secondTenantId, requestKey));

            // El índice es (TenantId, RequestKey), no (RequestKey): dos tenants distintos pueden
            // reusar la misma clave sin pisarse.
            await db.SaveChangesAsync();

            await using var verifyDb = CreateContext();
            var stored = await verifyDb
                .Reminders.IgnoreQueryFilters()
                .CountAsync(r => r.TenantId == firstTenantId || r.TenantId == secondTenantId);
            Assert.Equal(2, stored);
        }
        finally
        {
            await using var cleanupDb = CreateContext();
            await cleanupDb
                .Reminders.IgnoreQueryFilters()
                .Where(r => r.TenantId == firstTenantId || r.TenantId == secondTenantId)
                .ExecuteDeleteAsync();
        }
    }
}
