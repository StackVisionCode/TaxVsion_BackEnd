using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>COR-10 contra SQL Server, en una base temporal: la consulta de la bandeja y el índice único por buzón.</summary>
public sealed class MailboxOwnershipRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly string _connectionString = new SqlConnectionStringBuilder(CorrespondenceSqlServer.ConnectionString)
    {
        InitialCatalog = $"cor10_{Guid.NewGuid():N}"[..18],
    }.ConnectionString;

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private CorrespondenceDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenant()
        );

    private async Task SeedAsync(params MailboxOwnership[] rows)
    {
        await using var db = NewContext();
        var repository = new MailboxOwnershipRepository(db);
        foreach (var row in rows)
            await repository.AddAsync(row);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ListOwned_ReturnsTheUsersMailboxesInAnyState_WithoutATenantInContext()
    {
        var user = Guid.NewGuid();
        var active = Guid.NewGuid();
        var disconnected = Guid.NewGuid();
        await SeedAsync(
            MailboxOwnership.Create(_tenant, active, user, true, _now),
            MailboxOwnership.Create(_tenant, disconnected, user, false, _now),
            MailboxOwnership.Create(_tenant, Guid.NewGuid(), Guid.NewGuid(), true, _now),
            MailboxOwnership.Create(_tenant, Guid.NewGuid(), null, true, _now),
            MailboxOwnership.Create(Guid.NewGuid(), Guid.NewGuid(), user, true, _now)
        );

        await using var db = NewContext();
        var owned = await new MailboxOwnershipRepository(db).ListOwnedAccountIdsAsync(_tenant, user);

        Assert.Equivalent(new[] { active, disconnected }, owned);
    }

    [Fact]
    public async Task ListOwned_TenantWithRowsButNoneOfTheUser_ReturnsEmpty()
    {
        await SeedAsync(MailboxOwnership.Create(_tenant, Guid.NewGuid(), null, true, _now));

        await using var db = NewContext();
        var owned = await new MailboxOwnershipRepository(db).ListOwnedAccountIdsAsync(_tenant, Guid.NewGuid());

        Assert.NotNull(owned);
        Assert.Empty(owned);
    }

    [Fact]
    public async Task ListOwned_TenantWithoutRows_ReturnsNull()
    {
        await using var db = NewContext();

        Assert.Null(await new MailboxOwnershipRepository(db).ListOwnedAccountIdsAsync(_tenant, Guid.NewGuid()));
    }

    [Fact]
    public async Task AccountId_IsUniquePerTenant()
    {
        var account = Guid.NewGuid();
        await SeedAsync(MailboxOwnership.Create(_tenant, account, null, true, _now));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            SeedAsync(MailboxOwnership.Create(_tenant, account, Guid.NewGuid(), true, _now))
        );
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
