using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Queries;

public sealed record GetReceiptDownloadUrlQuery(Guid TenantId, Guid SaaSPaymentId);

public sealed record ReceiptDownloadUrlResponse(Uri DownloadUrl, DateTime ExpiresAtUtc);

/// <summary>
/// Descarga autenticada del recibo. PaymentApp valida que el pago es del tenant que pregunta y recién ahí le
/// pide a CloudStorage que firme la URL: el archivo no se sirve por acá y el enlace no queda expuesto.
/// </summary>
public static class GetReceiptDownloadUrlHandler
{
    public static async Task<Result<ReceiptDownloadUrlResponse>> Handle(
        GetReceiptDownloadUrlQuery query,
        ISaaSPaymentRepository payments,
        IReceiptDownloadUrlClient downloads,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(query.SaaSPaymentId, query.TenantId, ct);
        if (payment is null)
            return Result.Failure<ReceiptDownloadUrlResponse>(
                new Error("SaaSPayment.NotFound", "The payment does not exist for this tenant.")
            );

        if (payment.ReceiptFileId is not { } fileId)
            return Result.Failure<ReceiptDownloadUrlResponse>(
                new Error("Receipt.NotReady", "The receipt for this payment is not ready yet.")
            );

        var url = await downloads.GetAsync(query.TenantId, fileId, ct);

        return url.IsFailure
            ? Result.Failure<ReceiptDownloadUrlResponse>(url.Error)
            : Result.Success(new ReceiptDownloadUrlResponse(url.Value.Url, url.Value.ExpiresAtUtc));
    }
}
