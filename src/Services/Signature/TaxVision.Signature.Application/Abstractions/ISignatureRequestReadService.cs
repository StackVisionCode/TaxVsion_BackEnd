using TaxVision.Signature.Application.Requests.Queries.List;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Servicio de lectura optimizado para listados paginados de solicitudes de firma.
/// Devuelve DTOs livianos sin cargar los signers ni los campos.
/// </summary>
public interface ISignatureRequestReadService
{
    Task<ListSignatureRequestsResult> ListAsync(ListSignatureRequestsQuery query, CancellationToken ct = default);
}
