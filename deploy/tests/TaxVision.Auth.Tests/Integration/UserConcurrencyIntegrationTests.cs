using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>Contra SQL Server: dos ediciones del mismo usuario no se pisan en silencio.</summary>
public sealed class UserConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private AuthTestDatabase _database = null!;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        _database = await AuthTestDatabase.CreateAsync();
        var user = User.Register(
            _tenantId,
            "Ana",
            "Test",
            "ana@example.test",
            "hash",
            UserActorType.TenantEmployee
        ).Value;
        _userId = user.Id;
        await using var db = _database.NewContext(_tenantId);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task TwoContextsEditSameUser_SecondSaveConflicts()
    {
        await using var first = _database.NewContext(_tenantId);
        await using var second = _database.NewContext(_tenantId);
        var deactivating = await first.Users.SingleAsync(user => user.Id == _userId);
        var bumping = await second.Users.SingleAsync(user => user.Id == _userId);

        deactivating.Deactivate(Guid.NewGuid(), DateTime.UtcNow);
        bumping.BumpPermissionsVersion();

        await first.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
    }

    [Fact]
    public async Task RoleAssignmentAndProfileEdit_SecondSaveConflicts()
    {
        await using var first = _database.NewContext(_tenantId);
        await using var second = _database.NewContext(_tenantId);
        var assigning = await first.Users.SingleAsync(user => user.Id == _userId);
        var editing = await second.Users.SingleAsync(user => user.Id == _userId);

        // UserRoles es tabla hija: el salto de versión es lo que mueve la fila raíz.
        await new RoleRepository(first).ReplaceUserRolesAsync(_userId, [], null);
        assigning.BumpPermissionsVersion();
        Assert.True(editing.UpdateProfile("Ana", "Edited").IsSuccess);

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
    }
}
