namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Ciclo de vida individual de un firmante dentro de una <see cref="SignatureRequest"/>.
/// Transiciones válidas: <c>Pending → Signed | Rejected | Expired</c>.
/// </summary>
public enum SignerStatus
{
    Pending,
    Signed,
    Rejected,
    Expired,
}
