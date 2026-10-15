using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Infrastructure.Onboarding.Persistence.Repositories;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Persistence;

/// <summary>
/// Guardrail: un onboarding con pago cobrado NUNCA se expira — hay dinero del cliente involucrado,
/// la recuperación va por el admin recovery flow (OnboardingAdminController), no por el sweeper
/// que tumba onboardings viejos sin pago.
/// </summary>
public sealed class TenantOnboardingExpirationQueryTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static AuthDbContext CreateContext(string databaseName) =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(databaseName).Options,
            new FakeMessageBus(),
            new FakeTenantContext()
        );

    [Fact]
    public async Task GetExpiredPendingAsync_excludes_onboardings_that_already_paid()
    {
        var databaseName = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var createdLongAgo = now.AddHours(-13);

        var paid = TenantOnboarding
            .Create("paid@example.com", createdLongAgo, Guid.NewGuid(), "Paid", "Buyer", null, createdLongAgo)
            .Value;
        Assert.True(paid.MarkPaymentProcessing(Guid.NewGuid(), "stripe-pi-abc").IsSuccess);
        Assert.True(paid.MarkPaymentCompleted("stripe-pi-abc", now.AddHours(-1)).IsSuccess);

        var pendingNeverPaid = TenantOnboarding
            .Create("pending@example.com", createdLongAgo, Guid.NewGuid(), "Pending", "Buyer", null, createdLongAgo)
            .Value;

        await using (var seed = CreateContext(databaseName))
        {
            await seed.TenantOnboardings.AddRangeAsync(paid, pendingNeverPaid);
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        var repository = new TenantOnboardingRepository(db);

        var cutoff = now.AddMinutes(-30);
        var expired = await repository.GetExpiredPendingAsync(cutoff, batchSize: 100);

        Assert.Single(expired);
        Assert.Equal(pendingNeverPaid.Id, expired[0].Id);
        Assert.DoesNotContain(expired, candidate => candidate.Id == paid.Id);
    }
}
