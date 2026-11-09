using TaxVision.Scribe.Infrastructure.Seed;

namespace TaxVision.Scribe.Tests.Integration;

/// <summary>SCR-02 contra SQL real: mientras una réplica siembra, la otra no entra.</summary>
[Collection(ScribeTempDatabaseCollection.Name)]
public sealed class TemplateSeedLockIntegrationTests
{
    private const string Resource = "scribe:template-seed";

    [Fact]
    public async Task Second_replica_does_not_seed_while_first_holds_the_lock()
    {
        var first = new SqlSeedLock(ScribeTempDatabase.Connection);
        var second = new SqlSeedLock(ScribeTempDatabase.Connection);

        await using var held = await first.TryAcquireAsync(Resource, CancellationToken.None);

        Assert.NotNull(held);
        Assert.Null(await second.TryAcquireAsync(Resource, CancellationToken.None));
    }

    [Fact]
    public async Task Lock_is_free_again_after_the_first_replica_finishes()
    {
        var first = new SqlSeedLock(ScribeTempDatabase.Connection);
        var second = new SqlSeedLock(ScribeTempDatabase.Connection);

        var held = await first.TryAcquireAsync("scribe:template-seed-release", CancellationToken.None);
        Assert.NotNull(held);
        await held.DisposeAsync();

        await using var next = await second.TryAcquireAsync("scribe:template-seed-release", CancellationToken.None);
        Assert.NotNull(next);
    }
}
