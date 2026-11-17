using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;
using TaxVision.Auth.Onboarding.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Onboarding;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// La renovación de una reserva vencida se guarda: queda solo la reserva nueva, con su vencimiento y su referencia
/// de pago, y la generación avanza.
/// </summary>
public sealed class OnboardingCodeReservationRenewalIntegrationTests
{
    [Fact]
    public async Task Renewed_reservation_is_persisted_with_its_expiry_and_payment_reference()
    {
        var now = DateTime.UtcNow;
        var onboarding = OnboardingTestFactory.NewOnboarding(now.AddDays(-2));
        onboarding.ApplyOnboardingPricing(
            12_900,
            2_580,
            10_320,
            "USD",
            [
                new OnboardingCodeReservationInput(
                    Guid.NewGuid(),
                    OnboardingBenefitType.Promo,
                    "WELCOME20",
                    2_580,
                    "snap-1",
                    now.AddHours(-1),
                    Guid.NewGuid()
                ),
            ],
            now.AddDays(-2)
        );
        await using (var db = NewContext())
        {
            await new TenantOnboardingRepository(db).AddAsync(onboarding);
            await db.SaveChangesAsync();
        }

        var renewed = new OnboardingCodeReservationInput(
            Guid.NewGuid(),
            OnboardingBenefitType.Promo,
            "WELCOME20",
            2_580,
            "snap-2",
            now.AddHours(24),
            Guid.NewGuid()
        );
        await using (var db = NewContext())
        {
            var loaded = (await new TenantOnboardingRepository(db).GetByIdAsync(onboarding.Id))!;
            Assert.True(loaded.HasExpiredCodeReservations(now));
            Assert.True(loaded.RenewCodeReservations([renewed], now).IsSuccess);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var reloaded = (await new TenantOnboardingRepository(db).GetByIdAsync(onboarding.Id))!;
            var reservation = Assert.Single(reloaded.CodeReservations);
            Assert.Equal(renewed.CodeReservationId, reservation.CodeReservationId);
            Assert.Equal(renewed.PaymentReferenceId, reservation.PaymentReferenceId);
            Assert.Equal(renewed.ExpiresAtUtc!.Value, reservation.ExpiresAtUtc!.Value, TimeSpan.FromMilliseconds(1));
            Assert.Equal(1, reloaded.PricingGeneration);
            Assert.False(reloaded.HasExpiredCodeReservations(now));
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
