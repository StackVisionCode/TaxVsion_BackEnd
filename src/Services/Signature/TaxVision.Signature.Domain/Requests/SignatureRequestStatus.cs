namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Ciclo de vida de una <see cref="SignatureRequest"/>. Transiciones válidas:
/// <list type="bullet">
///   <item><c>Draft → Ready</c> cuando el documento original está disponible en CloudStorage.</item>
///   <item><c>Ready → InProgress</c> cuando el staff envía la solicitud.</item>
///   <item><c>InProgress → Completed</c> cuando todos los firmantes firman y el sealed está listo.</item>
///   <item><c>InProgress → Rejected</c> cuando cualquier firmante rechaza.</item>
///   <item><c>* → Canceled</c> (desde no-terminal) por acción del staff.</item>
///   <item><c>* → Expired</c> (desde no-terminal) cuando pasa el vencimiento.</item>
/// </list>
/// Terminales: <c>Completed</c>, <c>Rejected</c>, <c>Canceled</c>, <c>Expired</c>.
/// </summary>
public enum SignatureRequestStatus
{
    Draft,
    Ready,
    InProgress,
    Completed,
    Rejected,
    Canceled,
    Expired,
}
