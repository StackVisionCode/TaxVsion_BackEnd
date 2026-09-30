using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Cliente M2M del checkout de upgrade falso: devuelve el resultado configurado y captura el
/// último request.</summary>
public sealed class FakePlanChangeCheckoutPaymentClient(Result<PlanChangeCheckoutClientResult>? result = null)
    : IPlanChangeCheckoutPaymentClient
{
    private readonly Result<PlanChangeCheckoutClientResult> _result =
        result
        ?? Result.Success(
            new PlanChangeCheckoutClientResult(
                Guid.NewGuid(),
                "https://pay.example/upgrade",
                "cs_upgrade",
                DateTime.UtcNow.AddHours(24)
            )
        );

    public PlanChangeCheckoutClientRequest? LastRequest { get; private set; }

    public int CreateCalls { get; private set; }

    public Task<Result<PlanChangeCheckoutClientResult>> CreateCheckoutAsync(
        PlanChangeCheckoutClientRequest request,
        CancellationToken ct = default
    )
    {
        LastRequest = request;
        CreateCalls++;
        return Task.FromResult(_result);
    }
}
