using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Queries;

public sealed record SearchMySaaSPaymentsQuery(Guid TenantId, int Page, int PageSize);

/// <summary>
/// Una línea del historial de pagos del tenant. Deliberadamente más corta que la de admin: sin el código de
/// fallo crudo del proveedor ni la programación de reintentos, que son datos de soporte y no le dicen nada
/// útil a quien solo quiere ver qué pagó.
/// </summary>
public sealed record MySaaSPaymentResponse(
    Guid Id,
    string Status,
    string Type,
    long AmountCents,
    string Currency,
    long RefundedAmountCents,
    string ProviderCode,
    DateTime? PaidAtUtc,
    DateTime CreatedAtUtc,
    /// <summary>Si ya hay recibo para descargar. El PDF se genera después del cobro.</summary>
    bool HasReceipt
);

public sealed record MySaaSPaymentsPage(IReadOnlyList<MySaaSPaymentResponse> Items, int TotalCount);

public static class SearchMySaaSPaymentsHandler
{
    public static async Task<Result<MySaaSPaymentsPage>> Handle(
        SearchMySaaSPaymentsQuery query,
        ISaaSPaymentRepository payments,
        CancellationToken ct
    )
    {
        var (items, total) = await payments.SearchForTenantAsync(query.TenantId, query.Page, query.PageSize, ct);

        return Result.Success(new MySaaSPaymentsPage(items.Select(Map).ToList(), total));
    }

    private static MySaaSPaymentResponse Map(SaaSPayment payment) =>
        new(
            payment.Id,
            payment.Status.ToString(),
            payment.Type.ToString(),
            payment.Amount.AmountCents,
            payment.Amount.Currency,
            payment.Refunds.Sum(refund => refund.Amount.AmountCents),
            payment.ProviderCode.ToString(),
            payment.PaidAtUtc,
            payment.CreatedAtUtc,
            payment.ReceiptFileId is not null
        );
}
