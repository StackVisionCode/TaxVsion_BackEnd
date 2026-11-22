using System.Data.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Domain.ValueObjects;
using TaxVision.Tenant.Infrastructure.Persistence;
using TaxVision.Tenant.Infrastructure.Team;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Preferencias contra SQL Server (base temporal de la corrida), en una transacción que se revierte: ida y vuelta del
/// JSON, filtro global por oficina, unicidad con <c>UserId</c> nulo y borrado del retiro.
/// </summary>
public sealed class WorkspacePreferenceRepositoryIntegrationTests : IAsyncLifetime
{
    private const string Key = WorkspacePreferenceKeys.CrmDashboardLayout;
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private SqlConnection _connection = default!;
    private DbTransaction _transaction = default!;

    public async Task InitializeAsync()
    {
        _connection = new SqlConnection(TenantIntegrationDatabase.ConnectionString);
        await _connection.OpenAsync();
        _transaction = await _connection.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        await _transaction.RollbackAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_saved_layout_reads_back_with_its_canonical_json_and_version()
    {
        var saved = await SeedAsync(_tenantId, _userId, """[{ "id": "tasks", "size": "m", "hidden": true }]""");

        await using var db = NewContext();
        var read = await new WorkspacePreferenceRepository(db).GetForUserAsync(_tenantId, _userId, Key, default);

        Assert.NotNull(read);
        Assert.Equal(saved.Id, read.Id);
        Assert.Equal("""[{"id":"tasks","size":"m","hidden":true}]""", read.PayloadJson);
        Assert.Equal(1, read.Version);
        Assert.Equal(1, read.SchemaVersion);
        Assert.Equal(Now, read.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, read.UpdatedAtUtc.Kind);
    }

    [Fact]
    public async Task An_empty_row_keeps_a_null_payload()
    {
        var saved = await SeedAsync(_tenantId, _userId);
        await using (var db = NewContext())
        {
            var row = await new WorkspacePreferenceRepository(db).GetForUserAsync(_tenantId, _userId, Key, default);
            row!.Clear(1, Now.AddMinutes(1));
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var reread = await new WorkspacePreferenceRepository(read).GetForUserAsync(_tenantId, _userId, Key, default);
        Assert.Equal(saved.Id, reread!.Id);
        Assert.Null(reread.PayloadJson);
        Assert.Equal(2, reread.Version);
    }

    [Fact]
    public async Task The_user_row_and_the_office_default_are_read_apart()
    {
        var own = await SeedAsync(_tenantId, _userId);
        var office = await SeedAsync(_tenantId, null);

        await using var db = NewContext();
        var repository = new WorkspacePreferenceRepository(db);
        Assert.Equal(own.Id, (await repository.GetForUserAsync(_tenantId, _userId, Key, default))!.Id);
        Assert.Equal(office.Id, (await repository.GetOfficeDefaultAsync(_tenantId, Key, default))!.Id);
    }

    [Fact]
    public async Task Another_office_is_never_read_and_the_global_filter_isolates_the_ambient_office()
    {
        var mine = await SeedAsync(_tenantId, _userId);
        var other = Guid.NewGuid();
        await SeedAsync(other, _userId);
        await SeedAsync(other, null);

        await using var plain = NewContext();
        var repository = new WorkspacePreferenceRepository(plain);
        Assert.Null(await repository.GetForUserAsync(Guid.NewGuid(), _userId, Key, default));
        Assert.Null(await repository.GetOfficeDefaultAsync(_tenantId, Key, default));

        await using var ambient = NewContext(new FixedTenant(_tenantId));
        Assert.Equal([mine.Id], await ambient.WorkspacePreferences.Select(p => p.Id).ToListAsync());

        await using var none = NewContext();
        Assert.Empty(await none.WorkspacePreferences.ToListAsync());
    }

    [Fact]
    public async Task A_second_office_default_for_the_same_key_hits_the_unique_index()
    {
        await SeedAsync(_tenantId, null);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => SeedAsync(_tenantId, null));

        Assert.Equal("Persistence.UniqueConstraint", conflict.Code);
    }

    [Fact]
    public async Task A_second_row_for_the_same_user_and_key_hits_the_unique_index()
    {
        await SeedAsync(_tenantId, _userId);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => SeedAsync(_tenantId, _userId));

        Assert.Equal("Persistence.UniqueConstraint", conflict.Code);
    }

    [Fact]
    public async Task Deleting_a_user_removes_only_their_rows_in_that_office()
    {
        await SeedAsync(_tenantId, _userId);
        var colleague = await SeedAsync(_tenantId, Guid.NewGuid());
        var office = await SeedAsync(_tenantId, null);
        var elsewhere = await SeedAsync(Guid.NewGuid(), _userId);

        await using (var db = NewContext())
        {
            Assert.Equal(
                1,
                await new WorkspacePreferenceRepository(db).DeleteAllOfUserAsync(_tenantId, _userId, default)
            );
            Assert.Equal(
                0,
                await new WorkspacePreferenceRepository(db).DeleteAllOfUserAsync(_tenantId, _userId, default)
            );
        }

        await using var read = NewContext();
        var left = await read.WorkspacePreferences.IgnoreQueryFilters().Select(p => p.Id).ToListAsync();
        Assert.Equal(new[] { colleague.Id, office.Id, elsewhere.Id }.Order(), left.Where(Seeded).Order());
    }

    private readonly HashSet<Guid> _seeded = [];

    private bool Seeded(Guid id) => _seeded.Contains(id);

    private async Task<WorkspacePreference> SeedAsync(
        Guid tenantId,
        Guid? userId,
        string json = """[{ "id": "tasks", "size": "m", "hidden": false }]"""
    )
    {
        var preference = WorkspacePreference
            .Create(tenantId, userId, Key, DashboardLayout.Parse(1, json).Value, Now)
            .Value;
        await using var db = NewContext();
        new WorkspacePreferenceRepository(db).Add(preference);
        await db.SaveChangesAsync();
        _seeded.Add(preference.Id);
        return preference;
    }

    private TenantDbContext NewContext(ITenantContext? tenant = null)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlServer(_connection).Options;
        var db = new TenantDbContext(options, tenant ?? new FixedTenant(null));
        db.Database.UseTransaction(_transaction);
        return db;
    }

    private sealed class FixedTenant(Guid? tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId ?? Guid.Empty;
        public bool HasTenant => tenantId is not null;

        public void SetTenant(Guid id) { }
    }
}
