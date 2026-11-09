using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>Contra SQL Server: dos ediciones del mismo rol no se pisan en silencio.</summary>
public sealed class RoleConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private AuthTestDatabase _database = null!;
    private Guid _roleId;

    public async Task InitializeAsync()
    {
        _database = await AuthTestDatabase.CreateAsync();
        var role = Role.Create(_tenantId, "Reviewers", null).Value;
        _roleId = role.Id;
        await using var db = _database.NewContext(_tenantId);
        db.Roles.Add(role);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task TwoEditsOfSameRole_SecondSaveConflicts()
    {
        await using var first = _database.NewContext(_tenantId);
        await using var second = _database.NewContext(_tenantId);
        var renaming = await first.Roles.SingleAsync(role => role.Id == _roleId);
        var alsoRenaming = await second.Roles.SingleAsync(role => role.Id == _roleId);

        Assert.True(renaming.Update("First", null).IsSuccess);
        Assert.True(alsoRenaming.Update("Second", null).IsSuccess);

        await first.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
    }

    [Fact]
    public async Task PermissionChangeAndRename_SecondSaveConflicts()
    {
        await using var first = _database.NewContext(_tenantId);
        await using var second = _database.NewContext(_tenantId);
        var settingPermissions = await first
            .Roles.Include(role => role.Permissions)
            .SingleAsync(role => role.Id == _roleId);
        var renaming = await second.Roles.SingleAsync(role => role.Id == _roleId);

        // Cambiar los permisos toca la tabla hija; PermissionsVersion mueve la fila raíz.
        Assert.True(settingPermissions.SetPermissions([]).IsSuccess);
        Assert.True(renaming.Update("Renamed", null).IsSuccess);

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
    }
}
