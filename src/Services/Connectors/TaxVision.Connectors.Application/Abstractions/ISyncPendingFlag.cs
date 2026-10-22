namespace TaxVision.Connectors.Application.Abstractions;

/// <summary>
/// Marca "llegó un aviso mientras otro sync tenía el lock" por cuenta. Quien tiene el lock la consume al
/// terminar y hace una pasada más; sin esto ese aviso se perdía hasta la próxima reconciliación.
/// </summary>
public interface ISyncPendingFlag
{
    Task MarkAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>true si había marca (y la borra): hay que sincronizar otra vez.</summary>
    Task<bool> TryConsumeAsync(Guid accountId, CancellationToken ct = default);
}
