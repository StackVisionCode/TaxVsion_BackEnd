using Microsoft.EntityFrameworkCore;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.Webhooks;

namespace TaxVision.Sms.Infrastructure.Persistence.Repositories;

public sealed class SmsMessageRepository(SmsDbContext db) : ISmsMessageRepository
{
    public Task<SmsMessage?> GetByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct = default) =>
        db.SmsMessages.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.IdempotencyKey == idempotencyKey, ct);

    // Webhook: sin tenant en contexto → cross-tenant con IgnoreQueryFilters.
    public Task<SmsMessage?> GetByProviderMessageIdAsync(string providerCode, string providerMessageId, CancellationToken ct = default) =>
        db.SmsMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.ProviderCode == providerCode && m.ProviderMessageId == providerMessageId, ct);

    public Task<SmsMessage?> GetLatestByPhoneAsync(string phoneE164, CancellationToken ct = default) =>
        db.SmsMessages
            .IgnoreQueryFilters()
            .Where(m => m.To == phoneE164)
            .OrderByDescending(m => m.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(SmsMessage message, CancellationToken ct = default) =>
        await db.SmsMessages.AddAsync(message, ct);
}

public sealed class SmsOptOutRepository(SmsDbContext db) : ISmsOptOutRepository
{
    public Task<SmsOptOut?> GetAsync(Guid tenantId, Guid customerId, string phoneE164, CancellationToken ct = default) =>
        db.SmsOptOuts.FirstOrDefaultAsync(
            o => o.TenantId == tenantId && o.CustomerId == customerId && o.PhoneE164 == phoneE164,
            ct
        );

    // Webhook inbound: sin tenant en contexto → IgnoreQueryFilters, acotado por (tenant, phone).
    public Task<SmsOptOut?> GetByTenantAndPhoneAsync(Guid tenantId, string phoneE164, CancellationToken ct = default) =>
        db.SmsOptOuts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.PhoneE164 == phoneE164, ct);

    public async Task AddAsync(SmsOptOut optOut, CancellationToken ct = default) =>
        await db.SmsOptOuts.AddAsync(optOut, ct);
}

public sealed class ProcessedWebhookRepository(SmsDbContext db) : IProcessedWebhookRepository
{
    public Task<bool> ExistsAsync(string providerCode, string providerMessageId, string eventType, CancellationToken ct = default) =>
        db.ProcessedWebhooks.AnyAsync(
            p => p.ProviderCode == providerCode && p.ProviderMessageId == providerMessageId && p.EventType == eventType,
            ct
        );

    public async Task AddAsync(ProcessedWebhook processed, CancellationToken ct = default) =>
        await db.ProcessedWebhooks.AddAsync(processed, ct);
}
