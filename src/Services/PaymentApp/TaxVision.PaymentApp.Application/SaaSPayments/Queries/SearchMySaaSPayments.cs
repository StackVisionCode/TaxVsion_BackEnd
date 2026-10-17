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
    bool HasReceipt,
    /// <summary>true cuando esta fila sigue siendo pagable: no hay otro pago Succeeded posterior del
    /// mismo (Type, TargetAggregateId). El UI usa este flag para no mostrar "Pay now" en filas
    /// Failed que ya quedaron saldadas por una renovación/reintento posterior.</summary>
    bool CanRetry
);

public sealed record MySaaSPaymentsPage(IReadOnlyList<MySaaSPaymentResponse> Items, int TotalCount);

public static class SearchMySaaSPaymentsHandler
{
    // Los únicos tipos "pagables" desde el historial. Otros (Refund, ChargeBack, OnboardingInitial,
    // WalletTopUp) no abren recovery, así que no interrogamos pagos posteriores por ellos.
    private static readonly HashSet<SaaSPaymentType> RetryableTypes =
    [
        SaaSPaymentType.SubscriptionRenewal,
        SaaSPaymentType.SubscriptionRenewalCheckout,
        SaaSPaymentType.SeatRenewal,
        SaaSPaymentType.SeatsPurchaseCharge,
        SaaSPaymentType.AddOnRenewal,
        SaaSPaymentType.AddOnPurchaseCharge,
    ];

    public static async Task<Result<MySaaSPaymentsPage>> Handle(
        SearchMySaaSPaymentsQuery query,
        ISaaSPaymentRepository payments,
        CancellationToken ct
    )
    {
        var (items, total) = await payments.SearchForTenantAsync(query.TenantId, query.Page, query.PageSize, ct);

        // Para las filas Failed/ChargedBack de tipo pagable, preguntamos si ya hay un Succeeded
        // posterior sobre el mismo target. Si lo hay, la fila es zombi: UI apaga Pay now.
        var candidates = items
            .Where(payment =>
                (payment.Status == PaymentStatus.Failed || payment.Status == PaymentStatus.ChargedBack)
                && RetryableTypes.Contains(payment.Type)
            )
            .Select(payment => (payment.Type, payment.TargetAggregateId))
            .Distinct()
            .ToArray();

        var latestSucceeded =
            candidates.Length == 0
                ? new Dictionary<(SaaSPaymentType, Guid), DateTime>()
                : await payments.GetLatestSucceededCreatedAtByTargetsAsync(query.TenantId, candidates, ct);

        return Result.Success(
            new MySaaSPaymentsPage(items.Select(payment => Map(payment, latestSucceeded)).ToList(), total)
        );
    }

    private static MySaaSPaymentResponse Map(
        SaaSPayment payment,
        IReadOnlyDictionary<(SaaSPaymentType Type, Guid TargetAggregateId), DateTime> latestSucceeded
    )
    {
        var canRetry = ComputeCanRetry(payment, latestSucceeded);
        return new(
            payment.Id,
            payment.Status.ToString(),
            payment.Type.ToString(),
            payment.Amount.AmountCents,
            payment.Amount.Currency,
            payment.Refunds.Sum(refund => refund.Amount.AmountCents),
            payment.ProviderCode.ToString(),
            payment.PaidAtUtc,
            payment.CreatedAtUtc,
            payment.ReceiptFileId is not null,
            canRetry
        );
    }

    private static bool ComputeCanRetry(
        SaaSPayment payment,
        IReadOnlyDictionary<(SaaSPaymentType Type, Guid TargetAggregateId), DateTime> latestSucceeded
    )
    {
        if (payment.Status != PaymentStatus.Failed && payment.Status != PaymentStatus.ChargedBack)
            return false;
        if (!RetryableTypes.Contains(payment.Type))
            return false;
        if (!latestSucceeded.TryGetValue((payment.Type, payment.TargetAggregateId), out var latest))
            return true;
        // Hay un Succeeded sobre el mismo target posterior a esta fila: la fila quedó saldada.
        return latest <= payment.CreatedAtUtc;
    }
}
