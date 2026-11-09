using Microsoft.EntityFrameworkCore;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.Webhooks;

namespace TaxVision.Sms.Infrastructure.Persistence.Repositories;

public sealed class SmsMessageRepository(SmsDbContext db) : ISmsMessageRepository
{
    public Task<SmsMessage?> GetByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default
    ) =>
        // Tenant explícito: no depende de que el scope del comando local tenga el tenant cargado.
        db
            .SmsMessages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.IdempotencyKey == idempotencyKey, ct);

    // Webhook: sin tenant en contexto → cross-tenant con IgnoreQueryFilters.
    public Task<SmsMessage?> GetByProviderMessageIdAsync(
        string providerCode,
        string providerMessageId,
        CancellationToken ct = default
    ) =>
        db
            .SmsMessages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.ProviderCode == providerCode && m.ProviderMessageId == providerMessageId, ct);

    public Task<SmsMessage?> GetLatestByPhoneAsync(string phoneE164, CancellationToken ct = default) =>
        db
            .SmsMessages.IgnoreQueryFilters()
            .Where(m => m.To == phoneE164)
            .OrderByDescending(m => m.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<SmsPhoneTenant>> ListTenantsByPhoneAsync(
        string phoneE164,
        CancellationToken ct = default
    ) =>
        await db
            .SmsMessages.IgnoreQueryFilters()
            .Where(m => m.To == phoneE164)
            .GroupBy(m => m.TenantId)
            .Select(g =>
                g.OrderByDescending(m => m.CreatedAtUtc)
                    .Select(m => new SmsPhoneTenant(m.TenantId, m.CustomerId))
                    .First()
            )
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SmsMessage>> GetStuckForReconciliationAsync(
        Guid? tenantId,
        DateTime olderThanUtc,
        int limit,
        CancellationToken ct = default
    )
    {
        var query = db
            .SmsMessages.IgnoreQueryFilters()
            .Where(m =>
                (m.Status == SmsMessageStatus.Accepted || m.Status == SmsMessageStatus.OutcomeUnknown)
                && m.ProviderMessageId != null
                && m.UpdatedAtUtc < olderThanUtc
            );
        if (tenantId is { } t)
            query = query.Where(m => m.TenantId == t);
        return await query.OrderBy(m => m.UpdatedAtUtc).Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SmsMessage>> GetUnconfirmedSendsAsync(
        Guid? tenantId,
        DateTime pendingBeforeUtc,
        DateTime unknownBeforeUtc,
        int limit,
        CancellationToken ct = default
    )
    {
        var query = db
            .SmsMessages.IgnoreQueryFilters()
            .Where(m =>
                (
                    m.Status == SmsMessageStatus.Pending
                    && m.ProviderMessageId == null
                    && m.SendAttemptedAtUtc < pendingBeforeUtc
                ) || (m.Status == SmsMessageStatus.OutcomeUnknown && m.SendAttemptedAtUtc < unknownBeforeUtc)
            );
        if (tenantId is { } t)
            query = query.Where(m => m.TenantId == t);
        return await query.OrderBy(m => m.SendAttemptedAtUtc).Take(limit).ToListAsync(ct);
    }

    public Task<SmsMessage?> GetByIdAsync(Guid tenantId, Guid messageId, CancellationToken ct = default) =>
        db.SmsMessages.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == messageId, ct);

    public async Task<IReadOnlyList<SmsMessage>> ListByBatchAsync(
        Guid tenantId,
        Guid batchId,
        CancellationToken ct = default
    ) =>
        await db
            .SmsMessages.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.BatchId == batchId)
            .ToListAsync(ct);

    public async Task AddAsync(SmsMessage message, CancellationToken ct = default) =>
        await db.SmsMessages.AddAsync(message, ct);
}

public sealed class SmsOptOutRepository(SmsDbContext db) : ISmsOptOutRepository
{
    public Task<SmsOptOut?> GetAsync(
        Guid tenantId,
        Guid customerId,
        string phoneE164,
        CancellationToken ct = default
    ) =>
        db.SmsOptOuts.FirstOrDefaultAsync(
            o => o.TenantId == tenantId && o.CustomerId == customerId && o.PhoneE164 == phoneE164,
            ct
        );

    // Tenant explícito con IgnoreQueryFilters: el gate no debe depender del contexto (webhook, consumer).
    // También mira las filas aún sin guardar del mismo lote.
    public async Task<bool> IsPhoneOptedOutAsync(Guid tenantId, string phoneE164, CancellationToken ct = default) =>
        db.SmsOptOuts.Local.Any(o =>
            o.TenantId == tenantId && o.PhoneE164 == phoneE164 && o.Status == SmsOptOutStatus.OptedOut
        )
        || await db
            .SmsOptOuts.IgnoreQueryFilters()
            .AnyAsync(
                o => o.TenantId == tenantId && o.PhoneE164 == phoneE164 && o.Status == SmsOptOutStatus.OptedOut,
                ct
            );

    public async Task<IReadOnlyList<SmsOptOut>> ListByTenantAndPhoneAsync(
        Guid tenantId,
        string phoneE164,
        CancellationToken ct = default
    )
    {
        await db
            .SmsOptOuts.IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantId && o.PhoneE164 == phoneE164)
            .LoadAsync(ct);
        // Local = lo cargado más lo agregado y aún no guardado (evita duplicar la fila en el mismo lote).
        return db.SmsOptOuts.Local.Where(o => o.TenantId == tenantId && o.PhoneE164 == phoneE164).ToList();
    }

    public async Task AddAsync(SmsOptOut optOut, CancellationToken ct = default) =>
        await db.SmsOptOuts.AddAsync(optOut, ct);
}

public sealed class ProcessedWebhookRepository(SmsDbContext db) : IProcessedWebhookRepository
{
    public Task<bool> ExistsAsync(
        string providerCode,
        string providerMessageId,
        string eventType,
        CancellationToken ct = default
    ) =>
        db.ProcessedWebhooks.AnyAsync(
            p => p.ProviderCode == providerCode && p.ProviderMessageId == providerMessageId && p.EventType == eventType,
            ct
        );

    public async Task AddAsync(ProcessedWebhook processed, CancellationToken ct = default) =>
        await db.ProcessedWebhooks.AddAsync(processed, ct);
}

public sealed class PendingDeliveryReceiptRepository(SmsDbContext db) : IPendingDeliveryReceiptRepository
{
    public Task<bool> ExistsAsync(
        string providerCode,
        string providerMessageId,
        string eventType,
        CancellationToken ct = default
    ) =>
        db.PendingDeliveryReceipts.AnyAsync(
            r => r.ProviderCode == providerCode && r.ProviderMessageId == providerMessageId && r.EventType == eventType,
            ct
        );

    public async Task AddAsync(PendingDeliveryReceipt receipt, CancellationToken ct = default) =>
        await db.PendingDeliveryReceipts.AddAsync(receipt, ct);

    public async Task<IReadOnlyList<PendingDeliveryReceipt>> ListAsync(
        string providerCode,
        string providerMessageId,
        CancellationToken ct = default
    ) =>
        await db
            .PendingDeliveryReceipts.Where(r =>
                r.ProviderCode == providerCode && r.ProviderMessageId == providerMessageId
            )
            .OrderBy(r => r.ReceivedAtUtc)
            .ToListAsync(ct);

    public void Remove(PendingDeliveryReceipt receipt) => db.PendingDeliveryReceipts.Remove(receipt);

    public Task<int> DeleteReceivedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        db.PendingDeliveryReceipts.Where(r => r.ReceivedAtUtc < cutoffUtc).ExecuteDeleteAsync(ct);
}
