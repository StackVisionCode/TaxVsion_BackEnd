using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Wallet.Infrastructure.Persistence;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>Contextos de Wallet sobre la base temporal de las T-INT (BB-10).</summary>
public static class WalletTestDatabase
{
    public static string ConnectionString => WalletIntegrationDatabase.ConnectionString;

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
