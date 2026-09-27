using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;
using Xunit;

namespace TaxVision.Auth.Tests.Persistence;

/// <summary>
/// Los dispositivos de confianza se leen por <c>userId</c>, no por el tenant ambiente.
///
/// Encontrado en vivo: la pantalla de seguridad decía "No trusted devices" con la fila en la base, y
/// el botón "Remove" contestaba <c>Mfa.DeviceNotFound</c> — los dos síntomas de una misma causa.
/// <c>GetTrustedDevicesAsync</c> era la única lectura del repositorio sin <c>IgnoreQueryFilters()</c>,
/// así que caía en el filtro fail-closed: los handlers corren en el scope de DI de Wolverine, donde
/// no se puede confiar en que llegue el <c>TenantContext</c> de la request, y el filtro compara
/// contra <c>Guid.Empty</c>. Los métodos MFA y los códigos de recuperación sí se veían porque esas
/// dos lecturas sí lo ignoraban — de ahí que la pantalla se viera "a medias".
/// </summary>
public sealed class TrustedDeviceReadsTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    /// <param name="tenantInContext">Null = el escenario real del handler: scope sin tenant.</param>
    private static AuthDbContext CreateContext(string databaseName, Guid? tenantInContext)
    {
        var tenantContext = new FakeTenantContext();
        if (tenantInContext is not null)
            tenantContext.SetTenant(tenantInContext.Value);

        return new AuthDbContext(
            new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(databaseName).Options,
            new FakeMessageBus(),
            tenantContext
        );
    }

    private static async Task SeedDeviceAsync(string databaseName)
    {
        await using var db = CreateContext(databaseName, Tenant);
        db.TrustedDevices.Add(TrustedDevice.Create(Tenant, User, "hash", "Chrome", TimeSpan.FromDays(30)));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_devices_of_a_user_are_readable_without_a_tenant_in_context()
    {
        var name = Guid.NewGuid().ToString();
        await SeedDeviceAsync(name);

        await using var db = CreateContext(name, tenantInContext: null);
        var devices = await new MfaRepository(db).GetTrustedDevicesAsync(User);

        var device = Assert.Single(devices);
        Assert.Equal(User, device.UserId);
    }

    [Fact]
    public async Task Another_users_devices_never_come_back()
    {
        // Ignorar el filtro del tenant solo es seguro porque el userId acota a un usuario, que
        // pertenece a un solo tenant. Si esto se rompiera, el bypass dejaría de ser defendible.
        var name = Guid.NewGuid().ToString();
        await SeedDeviceAsync(name);

        await using var db = CreateContext(name, tenantInContext: null);
        var devices = await new MfaRepository(db).GetTrustedDevicesAsync(Guid.NewGuid());

        Assert.Empty(devices);
    }
}
