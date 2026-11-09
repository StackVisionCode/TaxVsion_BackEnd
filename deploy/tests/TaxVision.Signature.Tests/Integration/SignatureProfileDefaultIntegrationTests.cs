using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Profiles;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>SIG-7 contra SQL real: una sola firma default por (tenant, dueño). Base temporal propia.</summary>
public sealed class SignatureProfileDefaultIntegrationTests : IAsyncLifetime
{
    private readonly SignatureTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoSetDefaultInParallel_OneDefaultRemains(bool personal)
    {
        Guid? owner = personal ? Guid.NewGuid() : null;
        var first = await SeedAsync(owner);
        var second = await SeedAsync(owner);

        await using var winner = _database.NewContext(_tenantId);
        await using var loser = _database.NewContext(_tenantId);
        Assert.True((await LoadAsync(winner, first)).MarkDefault().IsSuccess);
        Assert.True((await LoadAsync(loser, second)).MarkDefault().IsSuccess);
        await winner.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => loser.SaveChangesAsync());

        await using var read = _database.NewContext(_tenantId);
        var defaults = await read
            .SignatureProfiles.IgnoreQueryFilters()
            .Where(p => p.TenantId == _tenantId && p.OwnerUserId == owner && p.IsDefault)
            .Select(p => p.Id)
            .ToListAsync();
        Assert.Equal(first, Assert.Single(defaults));
    }

    // Ida y vuelta: cubre los dos órdenes de Id, porque EF no ordena los UPDATE por el índice.
    [Fact]
    public async Task MovingTheDefault_BothWays_DoesNotHitTheIndex()
    {
        var owner = Guid.NewGuid();
        var first = await SeedAsync(owner);
        var second = await SeedAsync(owner);

        await MoveDefaultAsync(first, second);
        await MoveDefaultAsync(second, first);
        await MoveDefaultAsync(first, second);

        await using var read = _database.NewContext(_tenantId);
        Assert.True((await LoadAsync(read, second)).IsDefault);
        Assert.False((await LoadAsync(read, first)).IsDefault);
    }

    private async Task MoveDefaultAsync(Guid other, Guid target)
    {
        await using var db = _database.NewContext(_tenantId);
        var profiles = new[] { await LoadAsync(db, other), await LoadAsync(db, target) };
        Assert.True(DefaultSignatureProfilePolicy.SetDefault(profiles, profiles[1]).IsSuccess);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedAsync(Guid? owner)
    {
        var profile = SignatureProfile
            .Create(_tenantId, Guid.NewGuid(), owner, "Signature", Guid.NewGuid(), 10, 10)
            .Value;
        await using var db = _database.NewContext(_tenantId);
        db.SignatureProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    private static Task<SignatureProfile> LoadAsync(SignatureDbContext db, Guid id) =>
        db.SignatureProfiles.IgnoreQueryFilters().SingleAsync(p => p.Id == id);
}
