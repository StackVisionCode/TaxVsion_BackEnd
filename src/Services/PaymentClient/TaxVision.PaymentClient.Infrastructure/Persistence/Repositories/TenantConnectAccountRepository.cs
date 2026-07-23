using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class TenantConnectAccountRepository(PaymentClientDbContext db) : ITenantConnectAccountRepository
{
    // IgnoreQueryFilters: este repo corre dentro de un handler de Wolverine (bus.InvokeAsync),
    // en un scope de DI distinto al de la request HTTP que pobló ITenantContext vía
    // JwtTenantContextMiddleware; el HasQueryFilter ambiental de PaymentClientDbContext ve
    // Guid.Empty ahí. tenantId ya viene explícito y validado desde el controller/evento.
    public Task<TenantConnectAccount?> GetByTenantAndProviderAsync(
        Guid tenantId,
        PaymentProviderCode code,
        CancellationToken ct = default
    ) =>
        db
            .TenantConnectAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(account => account.TenantId == tenantId && account.ProviderCode == code, ct);

    // Mismo motivo que PaymentLinkRepository.GetByTokenAsync: StripeConnectAccountId es un
    // value converter (columna escalar), no un owned type — hay que comparar el VO completo.
    public Task<TenantConnectAccount?> GetByStripeConnectAccountIdAsync(
        string stripeConnectAccountId,
        CancellationToken ct = default
    )
    {
        var idResult = StripeConnectAccountId.Create(stripeConnectAccountId);
        if (idResult.IsFailure)
            return Task.FromResult<TenantConnectAccount?>(null);

        return db.TenantConnectAccounts.FirstOrDefaultAsync(
            account => account.StripeConnectAccountId == idResult.Value,
            ct
        );
    }

    public async Task AddAsync(TenantConnectAccount account, CancellationToken ct = default) =>
        await db.TenantConnectAccounts.AddAsync(account, ct);
}
