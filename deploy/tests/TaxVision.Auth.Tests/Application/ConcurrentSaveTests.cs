using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Common;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Guardado que pierde una carrera de RowVersion (03 §18.4): false y lo pendiente descartado.</summary>
public sealed class ConcurrentSaveTests
{
    [Fact]
    public async Task A_concurrency_conflict_returns_false_and_discards_the_pending_changes()
    {
        var unitOfWork = new ThrowingUnitOfWork("Persistence.ConcurrencyConflict");

        var saved = await ConcurrentSave.TrySaveAsync(unitOfWork, CancellationToken.None);

        Assert.False(saved);
        Assert.True(unitOfWork.Discarded);
    }

    [Fact]
    public async Task Other_conflicts_are_not_swallowed()
    {
        var unitOfWork = new ThrowingUnitOfWork("Persistence.UniqueConstraint");

        await Assert.ThrowsAsync<ConflictException>(() =>
            ConcurrentSave.TrySaveAsync(unitOfWork, CancellationToken.None)
        );
        Assert.False(unitOfWork.Discarded);
    }

    private sealed class ThrowingUnitOfWork(string code) : IUnitOfWork, IPendingChangesDiscarder
    {
        public bool Discarded { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            throw new ConflictException(code, "conflict");

        public void DiscardPendingChanges() => Discarded = true;
    }
}
