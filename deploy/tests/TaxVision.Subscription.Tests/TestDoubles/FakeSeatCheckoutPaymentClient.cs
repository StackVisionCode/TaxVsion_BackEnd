using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Cliente M2M de checkout falso: devuelve el resultado configurado y captura el último request.</summary>
public sealed class FakeSeatCheckoutPaymentClient(Result<SeatCheckoutClientResult> result) : ISeatCheckoutPaymentClient
{
    public SeatCheckoutClientRequest? LastRequest { get; private set; }

    public SeatPaymentStatusResult? PaymentStatus { get; set; }

    public Task<Result<SeatCheckoutClientResult>> CreateCheckoutAsync(
        SeatCheckoutClientRequest request,
        CancellationToken ct = default
    )
    {
        LastRequest = request;
        return Task.FromResult(result);
    }

    public Task<SeatPaymentStatusResult?> GetPaymentStatusAsync(
        Guid tenantId,
        Guid saaSPaymentId,
        CancellationToken ct = default
    ) => Task.FromResult(PaymentStatus);
}
