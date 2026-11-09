using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Infrastructure.Persistence;

namespace TaxVision.Correspondence.Tests.Integration;

// Contextos sobre la base temporal de las T-INT (BB-10).
internal static class CorrespondenceSqlServer
{
    public static string ConnectionString =>
        CorrespondenceIntegrationDatabase.ConnectionString;

    public static CorrespondenceDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>().UseSqlServer(ConnectionString).Options,
            new FixedTenant(tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
