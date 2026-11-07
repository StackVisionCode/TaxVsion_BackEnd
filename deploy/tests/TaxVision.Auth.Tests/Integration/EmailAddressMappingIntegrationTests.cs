using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>Contra SQL Server: el value object vive en la misma columna y las consultas de siempre lo encuentran.</summary>
public sealed class EmailAddressMappingIntegrationTests : IAsyncLifetime
{
    private const string Email = "ana.mapping@example.test";
    private readonly Guid _tenantId = Guid.NewGuid();
    private AuthTestDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = await AuthTestDatabase.CreateAsync();
        await using var db = _database.NewContext(_tenantId);
        db.Users.Add(
            User.Register(
                _tenantId,
                "Ana",
                "Test",
                " Ana.Mapping@Example.TEST ",
                "hash",
                UserActorType.TenantEmployee
            ).Value
        );
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task EmailColumn_KeepsThePlainNormalizedText()
    {
        await using var db = _database.NewContext(_tenantId);

        var stored = await db.Database.SqlQueryRaw<string>("SELECT [Email] AS [Value] FROM [Users]").SingleAsync();

        Assert.Equal(Email, stored);
    }

    [Fact]
    public async Task RepositoryQueries_TranslateAgainstTheColumn()
    {
        await using var db = _database.NewContext(_tenantId);
        var users = new UserRepository(db);

        var found = await users.GetByEmailAsync(_tenantId, Email, UserAccountKind.Staff);
        var exists = await users.EmailExistsAsync(_tenantId, Email, UserAccountKind.Staff);
        var tenants = await users.GetActiveTenantIdsByEmailAsync(Email, UserAccountKind.Staff);
        var (searched, total) = await users.GetPagedAsync(_tenantId, 1, 10, "mapping@", null);
        var directory = await users.ListUsersForDirectoryReconciliationAsync(1, 10);

        Assert.Equal(Email, found!.Email.Value);
        Assert.True(exists);
        Assert.Equal(_tenantId, Assert.Single(tenants));
        Assert.Equal(1, total);
        Assert.Equal(Email, Assert.Single(searched).Email.Value);
        Assert.Equal(Email, Assert.Single(directory.Items).Email);
    }

    [Fact]
    public async Task LegacyStoredValue_StillLoads()
    {
        var id = Guid.NewGuid();
        await using (var db = _database.NewContext(_tenantId))
        {
            var user = User.Register(
                _tenantId,
                "Old",
                "Row",
                "old.row@example.test",
                "hash",
                UserActorType.TenantEmployee
            ).Value;
            id = user.Id;
            db.Users.Add(user);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE [Users] SET [Email] = N'old.row@' WHERE [Id] = {0}", id);
        }

        await using var reader = _database.NewContext(_tenantId);
        var loaded = await reader.Users.SingleAsync(user => user.Id == id);

        Assert.Equal("old.row@", loaded.Email.Value);
    }
}
