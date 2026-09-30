using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Application.Abstractions;

public sealed record ReceiptDownloadUrl(Uri Url, DateTime ExpiresAtUtc);

/// <summary>
/// M2M contra CloudStorage para firmar la descarga de un recibo. PaymentApp valida antes que el pago es de
/// quien pregunta; CloudStorage solo firma. Es el patrón de Correspondence con sus adjuntos.
/// </summary>
public interface IReceiptDownloadUrlClient
{
    Task<Result<ReceiptDownloadUrl>> GetAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
}
