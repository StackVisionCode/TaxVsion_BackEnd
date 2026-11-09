using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>SIG-5 contra SQL real: la edición del admin y la excepción de plan no se pisan. Base temporal propia.</summary>
public sealed class TenantSignatureSettingsConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly SignatureTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task AdminEditVsApplyPlanConstraints_SecondSaveConflicts()
    {
        await using (var seed = _database.NewContext(_tenantId))
        {
            seed.TenantSignatureSettings.Add(TenantSignatureSettings.CreateForNewTenant(_tenantId, "cipher").Value);
            await seed.SaveChangesAsync();
        }

        await using var admin = _database.NewContext(_tenantId);
        await using var platform = _database.NewContext(_tenantId);
        var edited = await LoadAsync(admin);
        var constrained = await LoadAsync(platform);

        edited.SetSendPartialCopyDefault(true);
        constrained.ApplyPlanConstraints(SignaturePlanConstraints.None());
        await admin.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => platform.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
    }

    private Task<TenantSignatureSettings> LoadAsync(SignatureDbContext db) =>
        db.TenantSignatureSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == _tenantId);
}
