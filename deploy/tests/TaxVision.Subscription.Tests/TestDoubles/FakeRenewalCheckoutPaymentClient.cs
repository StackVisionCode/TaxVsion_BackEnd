using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Cliente M2M de checkout de renovación falso: devuelve el resultado configurado y captura el último
/// request. Espejo de <see cref="FakeSeatCheckoutPaymentClient"/>.</summary>
public sealed class FakeRenewalCheckoutPaymentClient(Result<RenewalCheckoutClientResult> result)
    : IRenewalCheckoutPaymentClient
{
    public RenewalCheckoutClientRequest? LastRequest { get; private set; }

    public RenewalPaymentStatusResult? PaymentStatus { get; set; }

    public Task<Result<RenewalCheckoutClientResult>> CreateCheckoutAsync(
        RenewalCheckoutClientRequest request,
        CancellationToken ct = default
    )
    {
        LastRequest = request;
        return Task.FromResult(result);
    }

    public Task<RenewalPaymentStatusResult?> GetPaymentStatusAsync(
        Guid tenantId,
        Guid saaSPaymentId,
        CancellationToken ct = default
    ) => Task.FromResult(PaymentStatus);
}
