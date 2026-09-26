using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Cliente M2M de checkout de add-on falso: devuelve el resultado configurado y captura el último
/// request.</summary>
public sealed class FakeAddOnCheckoutPaymentClient(Result<AddOnCheckoutClientResult> result)
    : IAddOnCheckoutPaymentClient
{
    public AddOnCheckoutClientRequest? LastRequest { get; private set; }

    /// <summary>Cuántas sesiones se pidieron: una compra reutilizada no debe pedir otra.</summary>
    public int CreateCalls { get; private set; }

    public AddOnPaymentStatusResult? PaymentStatus { get; set; }

    public Task<Result<AddOnCheckoutClientResult>> CreateCheckoutAsync(
        AddOnCheckoutClientRequest request,
        CancellationToken ct = default
    )
    {
        LastRequest = request;
        CreateCalls++;
        return Task.FromResult(result);
    }

    public Task<AddOnPaymentStatusResult?> GetPaymentStatusAsync(
        Guid tenantId,
        Guid saaSPaymentId,
        CancellationToken ct = default
    ) => Task.FromResult(PaymentStatus);
}
