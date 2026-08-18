namespace TaxVision.Tasks.Application.Common.Abstractions;

/// <summary>Si Wolverine ya abrió una, devuelve un handle inerte: anidar transacciones tira.</summary>
public interface ITransactionalScope
{
    Task<ITransactionalScopeHandle> BeginAsync(CancellationToken ct = default);
}

public interface ITransactionalScopeHandle : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}
