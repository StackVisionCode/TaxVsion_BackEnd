using BuildingBlocks.Results;

namespace TaxVision.Subscription.Application.Abstractions;

/// <summary>Request M2M a PaymentApp para abrir el hosted-checkout de reactivación de un seat específico.
/// Separado de <see cref="SeatCheckoutClientRequest"/> para no confundir dos flujos distintos — este cobra
/// 1 período completo del seat suspended, no prorrateo.</summary>
public sealed record SeatReactivationCheckoutClientRequest(
    Guid TenantId,
    Guid SeatId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    string Provider,
    string Method
);

/// <summary>Cliente M2M contra PaymentApp para crear la sesión de checkout de reactivación de un seat
/// (<c>POST internal/seats/{seatId}/reactivate-checkout</c>). Reusa el pipeline M2M ServiceOnly con firma HMAC
/// del mismo molde que <see cref="ISeatCheckoutPaymentClient"/>.</summary>
public interface ISeatReactivationCheckoutClient
{
    Task<Result<SeatCheckoutClientResult>> CreateReactivationCheckoutAsync(
        SeatReactivationCheckoutClientRequest request,
        CancellationToken ct = default
    );
}
