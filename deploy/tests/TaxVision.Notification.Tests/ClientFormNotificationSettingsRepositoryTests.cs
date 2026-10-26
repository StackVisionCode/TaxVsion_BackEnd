using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Infrastructure.Persistence;

namespace TaxVision.Notification.Tests;

/// <summary>
/// El push de Client Forms va encendido por defecto: una oficina sin fila de ajustes lo recibe igual
/// que el default de Tasks; solo una fila explícita en false lo apaga.
/// </summary>
public sealed class ClientFormNotificationSettingsRepositoryTests
{
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static NotificationDbContext CreateContext(string databaseName) =>
        new(
            new DbContextOptionsBuilder<NotificationDbContext>().UseInMemoryDatabase(databaseName).Options,
            new NoTenantContext()
        );

    // La implementación es interna; se instancia por su contrato público.
    private static IClientFormNotificationSettingsRepository Repository(NotificationDbContext db) =>
        (IClientFormNotificationSettingsRepository)
            Activator.CreateInstance(
                typeof(NotificationDbContext).Assembly.GetType(
                    "TaxVision.Notification.Infrastructure.Persistence.Repositories.ClientFormNotificationSettingsRepository",
                    throwOnError: true
                )!,
                db
            )!;

    [Fact]
    public async Task Office_without_settings_row_gets_push()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());

        Assert.True(await Repository(db).IsPushEnabledAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Explicit_row_turned_off_disables_push()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        await using (var seed = CreateContext(databaseName))
        {
            seed.ClientFormNotificationSettingsProjections.Add(
                ClientFormNotificationSettingsProjection.Create(tenantId, false, DateTime.UtcNow)
            );
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        Assert.False(await Repository(db).IsPushEnabledAsync(tenantId));
    }
}
