using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Infrastructure.Persistence;
using TaxVision.Wallet.Infrastructure.Persistence.Repositories;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>
/// WAL-8 con SQL real. Base temporal propia: un descuadre sembrado en la base local dispararía la alerta del
/// Wallet en marcha. Se borra al arrancar, por si una corrida abortada la dejó, y al terminar.
/// </summary>
public sealed class WalletLedgerReconciliationIntegrationTests : IAsyncLifetime
{
    private const string DatabaseName = "wallet_wal8_reconciliation_it";

    private static readonly string Connection = new SqlConnectionStringBuilder(WalletTestDatabase.ConnectionString)
    {
        InitialCatalog = DatabaseName,
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await DropDatabaseAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await DropDatabaseAsync();
    }

    [Fact]
    public async Task MismatchedWallet_IsReported()
    {
        var mismatched = Guid.NewGuid();
        var balanced = Guid.NewGuid();
        await using (var db = NewContext())
        {
            // Dos abonos en el saldo y uno solo en el ledger: 100 contra 90.
            var wallet = WalletAggregate.Create(mismatched).Value;
            db.LedgerEntries.Add(wallet.Credit(Usd(90), "topup:a").Value);
            Assert.True(wallet.Credit(Usd(10), "topup:b").IsSuccess);
            db.Wallets.Add(wallet);

            var other = WalletAggregate.Create(balanced).Value;
            db.LedgerEntries.Add(other.Credit(Usd(500), "topup:c").Value);
            db.LedgerEntries.Add(other.Reserve(Usd(200), "wallet-reserve:d").Value);
            db.Wallets.Add(other);
            await db.SaveChangesAsync();
        }

        var mismatch = Assert.Single(await ListAsync());

        Assert.Equal(mismatched, mismatch.TenantId);
        Assert.True(mismatch.IsReconcilable);
        Assert.Equal((100, 90), (mismatch.PostedMicros, mismatch.LedgerPostedMicros));
    }

    [Fact]
    public async Task HeldMismatch_IsReported()
    {
        await using (var db = NewContext())
        {
            var wallet = WalletAggregate.Create(Guid.NewGuid()).Value;
            db.LedgerEntries.Add(wallet.Credit(Usd(500), "topup:a").Value);
            Assert.True(wallet.Reserve(Usd(200), "wallet-reserve:b").IsSuccess);
            db.Wallets.Add(wallet);
            await db.SaveChangesAsync();
        }

        var mismatch = Assert.Single(await ListAsync());

        Assert.Equal((200, 0), (mismatch.HeldMicros, mismatch.LedgerHeldMicros));
    }

    // Un saldo sin asientos también es un descuadre; un monedero nuevo en cero no lo es.
    [Fact]
    public async Task WalletWithoutLedger_IsReportedOnlyWithBalance()
    {
        var funded = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var wallet = WalletAggregate.Create(funded).Value;
            Assert.True(wallet.Credit(Usd(50), "topup:a").IsSuccess);
            db.Wallets.Add(wallet);
            db.Wallets.Add(WalletAggregate.Create(Guid.NewGuid()).Value);
            await db.SaveChangesAsync();
        }

        var mismatch = Assert.Single(await ListAsync());

        Assert.Equal(funded, mismatch.TenantId);
        Assert.Equal(0, mismatch.LedgerPostedMicros);
    }

    // Cuadra en la suma, pero el ledger no dice de qué moneda es cada asiento.
    [Fact]
    public async Task TenantWithTwoWallets_IsNotReconcilable()
    {
        var tenantId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var usd = WalletAggregate.Create(tenantId, "USD").Value;
            db.LedgerEntries.Add(usd.Credit(Usd(100), "topup:a").Value);
            db.Wallets.Add(usd);
            db.Wallets.Add(WalletAggregate.Create(tenantId, "MXN").Value);
            await db.SaveChangesAsync();
        }

        var mismatch = Assert.Single(await ListAsync());

        Assert.Equal(tenantId, mismatch.TenantId);
        Assert.Equal(2, mismatch.WalletCount);
        Assert.False(mismatch.IsReconcilable);
    }

    // Sin tenant en contexto, como en el job: si la consulta no ignorara el filtro global, daría 0 filas.
    private static async Task<IReadOnlyList<WalletBalanceMismatch>> ListAsync()
    {
        await using var db = NewContext();
        return await new WalletRepository(db).ListBalanceMismatchesAsync();
    }

    private static WalletDbContext NewContext() =>
        new(new DbContextOptionsBuilder<WalletDbContext>().UseSqlServer(Connection).Options, new NoTenant());

    private static async Task DropDatabaseAsync()
    {
        var master = new SqlConnectionStringBuilder(WalletTestDatabase.ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{DatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{DatabaseName}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
