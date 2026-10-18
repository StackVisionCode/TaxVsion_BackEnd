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
    /// <summary>true cuando esta fila sigue siendo pagable: no hay otro pago Succeeded posterior que la
    /// salde. El UI usa este flag para no mostrar "Pay now" en filas Failed que ya quedaron saldadas por
    /// una renovación/reintento posterior.</summary>
    bool CanRetry
);

public sealed record MySaaSPaymentsPage(IReadOnlyList<MySaaSPaymentResponse> Items, int TotalCount);

public static class SearchMySaaSPaymentsHandler
{
    // Tipos pagables del tenant que viven a nivel de TenantSubscription (hay una por tenant), por lo
    // que un Succeeded de cualquiera de estos tipos salda un Failed anterior aunque TargetAggregateId
    // sea distinto (el off-session apunta al TenantSubscriptionRenewal; el self-service apunta al
    // TenantSubscriptionRenewalCheckout). Comparar por target perdería la equivalencia y la fila
    // Failed quedaría zombie.
    private static readonly SaaSPaymentType[] TenantWideSubscriptionTypes =
    [
        SaaSPaymentType.SubscriptionRenewal,
        SaaSPaymentType.SubscriptionRenewalCheckout,
    ];

    // Tipos pagables donde el target importa (cada seat / add-on tiene su propio ciclo de vida):
    // un Succeeded no salda a otro seat/add-on, aunque sean del mismo tenant.
    private static readonly SaaSPaymentType[][] PerTargetRetryFamilies =
    [
        [SaaSPaymentType.AddOnRenewal, SaaSPaymentType.AddOnPurchaseCharge],
        [SaaSPaymentType.SeatRenewal, SaaSPaymentType.SeatsPurchaseCharge],
    ];

    private static IEnumerable<SaaSPaymentType> FamilyOf(SaaSPaymentType type)
    {
        foreach (var family in PerTargetRetryFamilies)
            if (Array.IndexOf(family, type) >= 0)
                return family;
        return [type];
    }

    private static bool IsTenantWideSubscription(SaaSPaymentType type) =>
        Array.IndexOf(TenantWideSubscriptionTypes, type) >= 0;

    private static bool IsRetryable(SaaSPaymentType type) =>
        IsTenantWideSubscription(type) || PerTargetRetryFamilies.Any(family => Array.IndexOf(family, type) >= 0);

    public static async Task<Result<MySaaSPaymentsPage>> Handle(
        SearchMySaaSPaymentsQuery query,
        ISaaSPaymentRepository payments,
        CancellationToken ct
    )
    {
        var (items, total) = await payments.SearchForTenantAsync(query.TenantId, query.Page, query.PageSize, ct);

        // Dos preguntas al repo: una tenant-wide para la familia subscription (comparte TenantSubscription
        // entre todos los targets), y otra por (type, target) para las familias seat/add-on.
        var failedOrChargedBack = items
            .Where(p =>
                (p.Status == PaymentStatus.Failed || p.Status == PaymentStatus.ChargedBack) && IsRetryable(p.Type)
            )
            .ToArray();

        var needsTenantWideLookup = failedOrChargedBack.Any(p => IsTenantWideSubscription(p.Type));
        var latestByType = needsTenantWideLookup
            ? await payments.GetLatestSucceededCreatedAtByTypesAsync(query.TenantId, TenantWideSubscriptionTypes, ct)
            : new Dictionary<SaaSPaymentType, DateTime>();

        var perTargetCandidates = failedOrChargedBack
            .Where(p => !IsTenantWideSubscription(p.Type))
            .SelectMany(p => FamilyOf(p.Type).Select(familyType => (familyType, p.TargetAggregateId)))
            .Distinct()
            .ToArray();

        var latestByTarget =
            perTargetCandidates.Length == 0
                ? new Dictionary<(SaaSPaymentType, Guid), DateTime>()
                : await payments.GetLatestSucceededCreatedAtByTargetsAsync(query.TenantId, perTargetCandidates, ct);

        return Result.Success(
            new MySaaSPaymentsPage(items.Select(payment => Map(payment, latestByType, latestByTarget)).ToList(), total)
        );
    }

    private static MySaaSPaymentResponse Map(
        SaaSPayment payment,
        IReadOnlyDictionary<SaaSPaymentType, DateTime> latestByType,
        IReadOnlyDictionary<(SaaSPaymentType Type, Guid TargetAggregateId), DateTime> latestByTarget
    )
    {
        var canRetry = ComputeCanRetry(payment, latestByType, latestByTarget);
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
        IReadOnlyDictionary<SaaSPaymentType, DateTime> latestByType,
        IReadOnlyDictionary<(SaaSPaymentType Type, Guid TargetAggregateId), DateTime> latestByTarget
    )
    {
        if (payment.Status != PaymentStatus.Failed && payment.Status != PaymentStatus.ChargedBack)
            return false;
        if (!IsRetryable(payment.Type))
            return false;

        if (IsTenantWideSubscription(payment.Type))
        {
            // Un Succeeded de cualquier type de la familia subscription (sin importar target) salda la fila.
            foreach (var familyType in TenantWideSubscriptionTypes)
                if (latestByType.TryGetValue(familyType, out var latest) && latest > payment.CreatedAtUtc)
                    return false;
            return true;
        }

        // Familias seat/add-on: el Succeeded tiene que ser del mismo target (otro seat ≠ este seat).
        foreach (var familyType in FamilyOf(payment.Type))
            if (
                latestByTarget.TryGetValue((familyType, payment.TargetAggregateId), out var latest)
                && latest > payment.CreatedAtUtc
            )
                return false;
        return true;
    }
}
