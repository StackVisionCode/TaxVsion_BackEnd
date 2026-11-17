using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Onboarding.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Onboarding;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// El job de vencimiento mide la edad desde el último checkout iniciado: un alta vieja que reintentó el pago
/// hace un momento no se vence.
/// </summary>
public sealed class OnboardingExpirationQueryIntegrationTests
{
    [Fact]
    public async Task Expiration_query_measures_from_the_last_checkout_start()
    {
        var now = DateTime.UtcNow;
        var retried = OnboardingTestFactory.NewOnboarding(now.AddHours(-2));
        var paymentId = Guid.NewGuid();
        Assert.True(retried.MarkPaymentProcessing(paymentId, paymentId.ToString("N")).IsSuccess);
        Assert.True(retried.MarkPaymentFailed("card_declined").IsSuccess);
        Assert.True(retried.ReopenForPaymentRetry(now, 3, TimeSpan.FromHours(72)).IsSuccess);

        var abandoned = OnboardingTestFactory.NewOnboarding(now.AddHours(-2));

        await using (var db = NewContext())
        {
            var repository = new TenantOnboardingRepository(db);
            await repository.AddAsync(retried);
            await repository.AddAsync(abandoned);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var due = await new TenantOnboardingRepository(db).GetExpiredPendingAsync(now.AddMinutes(-30), 100_000);
            var ids = due.Select(onboarding => onboarding.Id).ToHashSet();

            Assert.DoesNotContain(retried.Id, ids);
            Assert.Contains(abandoned.Id, ids);
        }
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
