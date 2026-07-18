using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Messages;

internal sealed class FakeProviderConnectionAuditLogRepository : IProviderConnectionAuditLogRepository
{
    public List<ProviderConnectionAuditLog> Entries { get; } = [];
    public List<(DateTime CutoffUtc, int BatchSize)> DeleteCalls { get; } = [];
    public Queue<int> DeleteResults { get; } = [];

    public Task AddAsync(ProviderConnectionAuditLog entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default)
    {
        DeleteCalls.Add((cutoffUtc, batchSize));
        return Task.FromResult(DeleteResults.Count > 0 ? DeleteResults.Dequeue() : 0);
    }
}

internal sealed class FakeMessageBodyRateLimiter : IMessageBodyRateLimiter
{
    public bool AllowNext { get; set; } = true;
    public List<(Guid TenantId, Guid AccountId)> Calls { get; } = [];

    public Task<bool> TryAcquireAsync(Guid tenantId, Guid accountId, CancellationToken ct = default)
    {
        Calls.Add((tenantId, accountId));
        return Task.FromResult(AllowNext);
    }
}

internal sealed class FakeAttachmentRateLimiter : IAttachmentRateLimiter
{
    public bool AllowNext { get; set; } = true;
    public List<Guid> Calls { get; } = [];

    public Task<bool> TryAcquireAsync(Guid tenantId, CancellationToken ct = default)
    {
        Calls.Add(tenantId);
        return Task.FromResult(AllowNext);
    }
}

internal sealed class FakeSendRateLimiter : ISendRateLimiter
{
    public bool AllowNext { get; set; } = true;
    public List<(Guid TenantId, Guid AccountId)> Calls { get; } = [];

    public Task<bool> TryAcquireAsync(Guid tenantId, Guid accountId, CancellationToken ct = default)
    {
        Calls.Add((tenantId, accountId));
        return Task.FromResult(AllowNext);
    }
}

internal sealed class FakeOutboundEmailProviderClient(ProviderCode providerCode) : IOutboundEmailProviderClient
{
    public ProviderCode ProviderCode { get; } = providerCode;
    public List<(Guid AccountId, string FromAddress, string? FromDisplayName, OutboundMessage Message)> Calls { get; } =
    [];
    public Func<OutboundMessage, SendMessageResult>? OnSend { get; set; }
    public OutboundEmailSendException? ThrowOnSend { get; set; }

    public Task<SendMessageResult> SendMessageAsync(
        Guid accountId,
        string fromAddress,
        string? fromDisplayName,
        OutboundMessage message,
        CancellationToken ct = default
    )
    {
        Calls.Add((accountId, fromAddress, fromDisplayName, message));
        if (ThrowOnSend is not null)
            throw ThrowOnSend;

        return Task.FromResult(
            OnSend?.Invoke(message) ?? new SendMessageResult("provider-msg-1", "provider-thread-1", DateTime.UtcNow)
        );
    }
}

internal sealed class FakeOutboundEmailProviderClientFactory(IOutboundEmailProviderClient client)
    : IOutboundEmailProviderClientFactory
{
    public Result<IOutboundEmailProviderClient> Resolve(ProviderCode providerCode) => Result.Success(client);
}
