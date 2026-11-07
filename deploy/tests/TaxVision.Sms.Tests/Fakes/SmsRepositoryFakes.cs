using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.Webhooks;

namespace TaxVision.Sms.Tests.Fakes;

internal sealed class FakeSmsMessageRepository : ISmsMessageRepository
{
    public List<SmsMessage> Added { get; } = [];
    private readonly List<SmsMessage> _byIdempotency = [];
    private readonly List<SmsMessage> _byProviderMessageId = [];
    private readonly List<SmsMessage> _phoneHistory = [];

    public void SeedForIdempotency(SmsMessage message) => _byIdempotency.Add(message);

    public void SeedForProviderMessageId(SmsMessage message) => _byProviderMessageId.Add(message);

    public void SeedLatestByPhone(SmsMessage? message)
    {
        if (message is not null)
            _phoneHistory.Add(message);
    }

    // Envíos previos al número (cualquier tenant), en orden de llegada.
    public void SeedPhoneHistory(SmsMessage message) => _phoneHistory.Add(message);

    public Task<SmsMessage?> GetByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default
    ) => Task.FromResult(All().FirstOrDefault(m => m.TenantId == tenantId && m.IdempotencyKey == idempotencyKey));

    public Task<SmsMessage?> GetByProviderMessageIdAsync(
        string providerCode,
        string providerMessageId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _byProviderMessageId
                .Concat(Added)
                .FirstOrDefault(m => m.ProviderCode == providerCode && m.ProviderMessageId == providerMessageId)
        );

    public Task<SmsMessage?> GetLatestByPhoneAsync(string phoneE164, CancellationToken ct = default) =>
        Task.FromResult(_phoneHistory.LastOrDefault(m => m.To == phoneE164));

    public Task<IReadOnlyList<SmsPhoneTenant>> ListTenantsByPhoneAsync(
        string phoneE164,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<SmsPhoneTenant>>([
            .. _phoneHistory
                .Where(m => m.To == phoneE164)
                .GroupBy(m => m.TenantId)
                .Select(g => new SmsPhoneTenant(g.Key, g.Last().CustomerId)),
        ]);

    public Task<IReadOnlyList<SmsMessage>> GetStuckForReconciliationAsync(
        Guid? tenantId,
        DateTime olderThanUtc,
        int limit,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<SmsMessage>>([
            .. _byProviderMessageId
                .Concat(Added)
                .Where(m =>
                    (m.Status == SmsMessageStatus.Accepted || m.Status == SmsMessageStatus.OutcomeUnknown)
                    && m.ProviderMessageId != null
                    && m.UpdatedAtUtc < olderThanUtc
                    && (tenantId is null || m.TenantId == tenantId)
                )
                .OrderBy(m => m.UpdatedAtUtc)
                .Take(limit),
        ]);

    public Task<IReadOnlyList<SmsMessage>> GetUnconfirmedSendsAsync(
        Guid? tenantId,
        DateTime pendingBeforeUtc,
        DateTime unknownBeforeUtc,
        int limit,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<SmsMessage>>([
            .. All()
                .Where(m =>
                    (
                        (
                            m.Status == SmsMessageStatus.Pending
                            && m.ProviderMessageId == null
                            && m.SendAttemptedAtUtc < pendingBeforeUtc
                        ) || (m.Status == SmsMessageStatus.OutcomeUnknown && m.SendAttemptedAtUtc < unknownBeforeUtc)
                    ) && (tenantId is null || m.TenantId == tenantId)
                )
                .OrderBy(m => m.SendAttemptedAtUtc)
                .Take(limit),
        ]);

    public Task<SmsMessage?> GetByIdAsync(Guid tenantId, Guid messageId, CancellationToken ct = default) =>
        Task.FromResult(All().FirstOrDefault(m => m.TenantId == tenantId && m.Id == messageId));

    public Task<IReadOnlyList<SmsMessage>> ListByBatchAsync(
        Guid tenantId,
        Guid batchId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<SmsMessage>>([
            .. All().Where(m => m.TenantId == tenantId && m.BatchId == batchId),
        ]);

    private IEnumerable<SmsMessage> All() => _byIdempotency.Concat(_byProviderMessageId).Concat(Added).Distinct();

    public Task AddAsync(SmsMessage message, CancellationToken ct = default)
    {
        Added.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class FakeSmsOptOutRepository : ISmsOptOutRepository
{
    public List<SmsOptOut> Added { get; } = [];
    private readonly List<SmsOptOut> _seed = [];

    public void Seed(SmsOptOut optOut) => _seed.Add(optOut);

    public Task<SmsOptOut?> GetAsync(
        Guid tenantId,
        Guid customerId,
        string phoneE164,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _seed
                .Concat(Added)
                .FirstOrDefault(o => o.TenantId == tenantId && o.CustomerId == customerId && o.PhoneE164 == phoneE164)
        );

    public Task<bool> IsPhoneOptedOutAsync(Guid tenantId, string phoneE164, CancellationToken ct = default) =>
        Task.FromResult(
            _seed.Concat(Added).Any(o => o.TenantId == tenantId && o.PhoneE164 == phoneE164 && o.IsOptedOut)
        );

    public Task<IReadOnlyList<SmsOptOut>> ListByTenantAndPhoneAsync(
        Guid tenantId,
        string phoneE164,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<SmsOptOut>>([
            .. _seed.Concat(Added).Where(o => o.TenantId == tenantId && o.PhoneE164 == phoneE164),
        ]);

    public Task AddAsync(SmsOptOut optOut, CancellationToken ct = default)
    {
        Added.Add(optOut);
        return Task.CompletedTask;
    }
}

internal sealed class FakePendingDeliveryReceiptRepository : IPendingDeliveryReceiptRepository
{
    public List<PendingDeliveryReceipt> Receipts { get; } = [];

    public Task<bool> ExistsAsync(
        string providerCode,
        string providerMessageId,
        string eventType,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Receipts.Exists(r =>
                r.ProviderCode == providerCode && r.ProviderMessageId == providerMessageId && r.EventType == eventType
            )
        );

    public Task AddAsync(PendingDeliveryReceipt receipt, CancellationToken ct = default)
    {
        Receipts.Add(receipt);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PendingDeliveryReceipt>> ListAsync(
        string providerCode,
        string providerMessageId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<PendingDeliveryReceipt>>([
            .. Receipts
                .Where(r => r.ProviderCode == providerCode && r.ProviderMessageId == providerMessageId)
                .OrderBy(r => r.ReceivedAtUtc),
        ]);

    public void Remove(PendingDeliveryReceipt receipt) => Receipts.Remove(receipt);

    public Task<int> DeleteReceivedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        Task.FromResult(Receipts.RemoveAll(r => r.ReceivedAtUtc < cutoffUtc));
}

internal sealed class FakeProcessedWebhookRepository : IProcessedWebhookRepository
{
    public List<ProcessedWebhook> Added { get; } = [];
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    private static string Key(string code, string pmid, string evt) => $"{code}|{pmid}|{evt}";

    public void SeedExists(string providerCode, string providerMessageId, string eventType) =>
        _seen.Add(Key(providerCode, providerMessageId, eventType));

    public Task<bool> ExistsAsync(
        string providerCode,
        string providerMessageId,
        string eventType,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _seen.Contains(Key(providerCode, providerMessageId, eventType))
                || Added.Any(p =>
                    p.ProviderCode == providerCode
                    && p.ProviderMessageId == providerMessageId
                    && p.EventType == eventType
                )
        );

    public Task AddAsync(ProcessedWebhook processed, CancellationToken ct = default)
    {
        Added.Add(processed);
        return Task.CompletedTask;
    }
}
