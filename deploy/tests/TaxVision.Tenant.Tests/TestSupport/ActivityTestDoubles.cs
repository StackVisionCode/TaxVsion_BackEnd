using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Tests.TestSupport;

/// <summary>Repositorio, unidad y correlación en memoria que anotan en un mismo registro el orden de las llamadas.</summary>
internal sealed class ActivityHarness : IActivityEntryRepository, IUnitOfWork, ICorrelationContext
{
    private readonly List<ActivityEntry> _pending = [];

    public List<string> Log { get; } = [];
    public List<ActivityEntry> Saved { get; } = [];
    public ActivityFeedFilter? LastFilter { get; private set; }

    public string CorrelationId => "test-correlation-id";

    public Task<bool> ExistsAsync(Guid tenantId, Guid eventId, CancellationToken ct = default)
    {
        Log.Add("exists");
        return Task.FromResult(Saved.Exists(e => e.TenantId == tenantId && e.EventId == eventId));
    }

    public void Add(ActivityEntry entry) => _pending.Add(entry);

    public Task<PagedResult<ActivityEntry>> ListAsync(ActivityFeedFilter filter, CancellationToken ct = default)
    {
        LastFilter = filter;
        return Task.FromResult(new PagedResult<ActivityEntry>(Saved, filter.Page, filter.Size, Saved.Count));
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Log.Add("save");
        Saved.AddRange(_pending);
        var count = _pending.Count;
        _pending.Clear();
        return Task.FromResult(count);
    }

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId)
    {
        Log.Add($"push:{correlationId}");
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>Un evento con su actor, el mismo sin actor y lo que debe quedar registrado.</summary>
public sealed record ActivityCase(
    IntegrationEvent Event,
    IntegrationEvent WithoutActor,
    Func<IntegrationEvent, IActivityEntryRepository, IUnitOfWork, ICorrelationContext, Task> Handle,
    Guid Actor,
    string Verb,
    Guid ResourceId,
    Guid? CustomerId,
    ActivityVisibility Visibility
);
