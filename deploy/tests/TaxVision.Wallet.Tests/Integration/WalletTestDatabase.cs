using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Wallet.Infrastructure.Persistence;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>Base local de Wallet para las T-INT: WALLET_DB_CONNECTION o los user-secrets de la Api.</summary>
public static class WalletTestDatabase
{
    private const string ApiUserSecretsId = "6fb4d16c-9644-4b66-b322-179dcdf03dfc";

    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("WALLET_DB_CONNECTION")
        ?? new ConfigurationBuilder().AddUserSecrets(ApiUserSecretsId).Build().GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set WALLET_DB_CONNECTION or the Wallet Api user-secrets to run this test."
        );

    public static WalletDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<WalletDbContext>().UseSqlServer(ConnectionString).Options,
            new FixedTenant(tenantId)
        );

    public sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
