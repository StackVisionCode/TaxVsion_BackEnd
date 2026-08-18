using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TaxVision.Tasks.Application.Common.Abstractions;

namespace TaxVision.Tasks.Infrastructure.Persistence;

public sealed class TransactionalScope(TasksDbContext context) : ITransactionalScope
{
    public async Task<ITransactionalScopeHandle> BeginAsync(CancellationToken ct = default)
    {
        // Wolverine ya abrió una si el handler entró por el bus; el commit es suyo, no nuestro.
        if (context.Database.CurrentTransaction is not null)
            return new AmbientHandle();

        return new OwnedHandle(await context.Database.BeginTransactionAsync(ct));
    }

    private sealed class OwnedHandle(IDbContextTransaction transaction) : ITransactionalScopeHandle
    {
        public Task CommitAsync(CancellationToken ct = default) => transaction.CommitAsync(ct);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    private sealed class AmbientHandle : ITransactionalScopeHandle
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
