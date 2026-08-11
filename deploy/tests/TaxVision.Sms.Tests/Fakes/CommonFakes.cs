using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using TaxVision.Sms.Application.Abstractions;

namespace TaxVision.Sms.Tests.Fakes;

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(0);
    }
}

internal sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = string.Empty;

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId)
    {
        var previous = CorrelationId;
        CorrelationId = correlationId;
        return new Popper(this, previous);
    }

    private sealed class Popper(FakeCorrelationContext owner, string previous) : IDisposable
    {
        public void Dispose() => owner.CorrelationId = previous;
    }
}

internal sealed class FakeSmsWebhookSecrets : ISmsWebhookSecrets
{
    public string? GetSecret(string providerCode) => "fake-secret";
}
