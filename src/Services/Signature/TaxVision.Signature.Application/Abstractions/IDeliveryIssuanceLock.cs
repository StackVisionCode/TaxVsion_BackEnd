namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// SIG-16: una sola emisión de entregas finales a la vez por solicitud. La segunda espera a que la primera
/// confirme y ve sus entregas, en vez de insertar otra vez y chocar con el índice único. Se suelta al confirmar.
/// </summary>
public interface IDeliveryIssuanceLock
{
    Task AcquireAsync(Guid signatureRequestId, CancellationToken ct = default);
}
