namespace TaxVision.Wallet.Tests.Integration;

public sealed class WalletTestDatabaseIntegrationTests
{
    [Fact]
    public async Task ConnectionString_FromUserSecrets_ConnectsToLocalDatabase()
    {
        await using var db = WalletTestDatabase.NewContext(Guid.NewGuid());

        Assert.True(await db.Database.CanConnectAsync());
    }
}
