namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Invalida la caché de la LISTA de solicitudes de firma de un tenant. Lo llaman los handlers que
/// cambian algo visible en el dashboard (crear, enviar, firmar, rechazar, cancelar) para que el
/// siguiente refresco —manual o disparado por realtime— devuelva datos frescos sin esperar al TTL.
/// </summary>
public interface ISignatureRequestListCacheInvalidator
{
    Task InvalidateAsync(Guid tenantId, CancellationToken ct = default);
}
