using BuildingBlocks.Common;
using BuildingBlocks.Persistence;

namespace TaxVision.Campaigns.Tests.Fakes;

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    /// <summary>La próxima confirmación falla, como un choque de la base.</summary>
    public bool FailNextSave { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new InvalidOperationException("Simulated save failure.");
        }

        SaveChangesCallCount++;
        return Task.FromResult(0);
    }
}

internal sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = "test-correlation";

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

/// <summary>Ventana de SMS por oficina en memoria.</summary>
internal sealed class FakeCampaignSmsWindowRepository
    : TaxVision.Campaigns.Application.Settings.SmsWindow.ICampaignSmsWindowRepository
{
    public List<TaxVision.Campaigns.Domain.Settings.CampaignSmsWindow> Windows { get; } = [];

    public Task<TaxVision.Campaigns.Domain.Settings.CampaignSmsWindow?> GetAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => Task.FromResult(Windows.Find(w => w.TenantId == tenantId));

    public void Add(TaxVision.Campaigns.Domain.Settings.CampaignSmsWindow window) => Windows.Add(window);

    public void Remove(TaxVision.Campaigns.Domain.Settings.CampaignSmsWindow window) => Windows.Remove(window);
}
