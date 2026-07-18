using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Tests.Ingest;

internal sealed class FakeUnmatchedIncomingEmailRepository : IUnmatchedIncomingEmailRepository
{
    private readonly List<UnmatchedIncomingEmail> _store = [];

    public IReadOnlyList<UnmatchedIncomingEmail> All => _store;

    public Task AddAsync(UnmatchedIncomingEmail entity, CancellationToken ct = default)
    {
        _store.Add(entity);
        return Task.CompletedTask;
    }
}
