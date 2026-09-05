using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.PaymentMethods;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

public sealed class OnboardingPaymentMethodOverrideRepository(PaymentAppDbContext db)
    : IOnboardingPaymentMethodOverrideRepository
{
    public async Task<IReadOnlyList<OnboardingPaymentMethodOverride>> ListAsync(CancellationToken ct = default) =>
        await db
            .OnboardingPaymentMethodOverrides.AsNoTracking()
            .OrderBy(x => x.ProviderCode)
            .ThenBy(x => x.Method)
            .ToListAsync(ct);

    public Task<OnboardingPaymentMethodOverride?> GetAsync(
        PaymentProviderCode providerCode,
        string method,
        CancellationToken ct = default
    ) =>
        db.OnboardingPaymentMethodOverrides.FirstOrDefaultAsync(
            x => x.ProviderCode == providerCode && x.Method == method,
            ct
        );

    public async Task AddAsync(OnboardingPaymentMethodOverride paymentMethodOverride, CancellationToken ct = default) =>
        await db.OnboardingPaymentMethodOverrides.AddAsync(paymentMethodOverride, ct);
}
