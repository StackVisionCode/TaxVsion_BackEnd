using TaxVision.Postmaster.Domain.Feedback;

namespace TaxVision.Postmaster.Application.Feedback;

public interface IDeliveryFeedbackReceiptRepository
{
    /// <summary>Atajo barato antes de trabajar; la garantía la da <see cref="TryCommitAsync"/>.</summary>
    Task<bool> ExistsAsync(string source, string sourceEventId, CancellationToken ct = default);

    /// <summary>
    /// Guarda el recibo junto con los cambios pendientes, en una sola transacción. Si el aviso ya estaba
    /// (otra copia llegó a la vez), descarta todo y devuelve false: decide el índice único.
    /// </summary>
    Task<bool> TryCommitAsync(DeliveryFeedbackReceipt receipt, CancellationToken ct = default);
}
