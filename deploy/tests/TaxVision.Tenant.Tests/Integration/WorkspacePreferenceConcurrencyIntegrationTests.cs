using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>Dos guardados válidos sobre la misma fila: la versión es el token de concurrencia. Base temporal propia.</summary>
public sealed class WorkspacePreferenceConcurrencyIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly TenantTemporaryDatabase _database = new();
    private Guid _preferenceId;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        var preference = WorkspacePreference
            .Create(Guid.NewGuid(), Guid.NewGuid(), WorkspacePreferenceKeys.CrmDashboardLayout, Layout("m"), Now)
            .Value;
        _preferenceId = preference.Id;
        await using var db = _database.NewContext();
        db.WorkspacePreferences.Add(preference);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DropAsync();

    [Fact]
    public async Task Two_saves_from_the_same_version_the_second_conflicts()
    {
        await using var first = _database.NewContext();
        await using var second = _database.NewContext();
        var fromFirstTab = await Load(first);
        var fromSecondTab = await Load(second);

        Assert.True(fromFirstTab.Replace(Layout("l"), 1, Now.AddMinutes(1)).Value);
        Assert.True(fromSecondTab.Replace(Layout("s"), 1, Now.AddMinutes(2)).Value);

        await first.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);

        await using var read = _database.NewContext();
        var saved = await read.WorkspacePreferences.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.Equal(2, saved.Version);
        Assert.Equal(Layout("l").ToCanonicalJson(), saved.PayloadJson);
    }

    private Task<WorkspacePreference> Load(TaxVision.Tenant.Infrastructure.Persistence.TenantDbContext db) =>
        db.WorkspacePreferences.IgnoreQueryFilters().SingleAsync(p => p.Id == _preferenceId);

    private static DashboardLayout Layout(string size) =>
        DashboardLayout.Parse(1, $$"""[{ "id": "tasks", "size": "{{size}}", "hidden": false }]""").Value;
}
