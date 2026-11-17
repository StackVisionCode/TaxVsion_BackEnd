using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;
using TaxVision.Auth.Onboarding.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Onboarding;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Tras chocar con otro proceso que guardó primero, releer el alta trae su estado vigente y el guardado
/// siguiente del mismo contexto ya no choca.
/// </summary>
public sealed class TenantOnboardingReloadIntegrationTests
{
    [Fact]
    public async Task Reload_after_a_conflict_returns_the_saved_state_and_lets_the_context_save()
    {
        var now = DateTime.UtcNow;
        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        var paymentId = Guid.NewGuid();
        Assert.True(onboarding.MarkPaymentProcessing(paymentId, paymentId.ToString("N")).IsSuccess);
        await using (var setup = NewContext())
        {
            await new TenantOnboardingRepository(setup).AddAsync(onboarding);
            await setup.SaveChangesAsync();
        }

        await using var db = NewContext();
        var repository = new TenantOnboardingRepository(db);
        var stale = (await repository.GetByIdAsync(onboarding.Id))!;

        await using (var other = NewContext())
        {
            var winner = (await new TenantOnboardingRepository(other).GetByIdAsync(onboarding.Id))!;
            Assert.True(winner.MarkPaymentCompleted(paymentId.ToString("N"), now).IsSuccess);
            await other.SaveChangesAsync();
        }

        Assert.True(stale.MarkPaymentFailed("card_declined").IsSuccess);
        await Assert.ThrowsAsync<ConflictException>(() => db.SaveChangesAsync());

        var current = (await repository.ReloadAsync(onboarding.Id))!;

        Assert.Equal(TenantOnboardingStatus.PaymentCompleted, current.Status);
        // El guardado final de la transacción no debe chocar otra vez con la copia vieja.
        await db.SaveChangesAsync();
    }

    private static AuthDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(AuthIntegrationDatabase.ConnectionString).Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
