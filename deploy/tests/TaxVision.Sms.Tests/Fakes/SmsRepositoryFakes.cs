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
    private SmsMessage? _latestByPhone;

    public void SeedForIdempotency(SmsMessage message) => _byIdempotency.Add(message);

    public void SeedForProviderMessageId(SmsMessage message) => _byProviderMessageId.Add(message);

    public void SeedLatestByPhone(SmsMessage? message) => _latestByPhone = message;

    public Task<SmsMessage?> GetByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _byIdempotency.FirstOrDefault(m => m.TenantId == tenantId && m.IdempotencyKey == idempotencyKey)
        );

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
        Task.FromResult(_latestByPhone);

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

    public Task<SmsOptOut?> GetByTenantAndPhoneAsync(Guid tenantId, string phoneE164, CancellationToken ct = default) =>
        Task.FromResult(_seed.Concat(Added).FirstOrDefault(o => o.TenantId == tenantId && o.PhoneE164 == phoneE164));

    public Task AddAsync(SmsOptOut optOut, CancellationToken ct = default)
    {
        Added.Add(optOut);
        return Task.CompletedTask;
    }
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
