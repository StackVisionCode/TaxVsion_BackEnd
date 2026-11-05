using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Correspondence.Infrastructure.Persistence;

namespace TaxVision.Correspondence.Tests.Integration;

// Base local: CORRESPONDENCE_SQLSERVER_TESTS o los user-secrets de la Api.
internal static class CorrespondenceSqlServer
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("CORRESPONDENCE_SQLSERVER_TESTS")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("236deafb-0353-4dac-8aed-b15fbb76ae20")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set CORRESPONDENCE_SQLSERVER_TESTS or the Correspondence Api user-secrets to run this test."
        );

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
