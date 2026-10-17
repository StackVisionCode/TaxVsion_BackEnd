using BuildingBlocks.PurchaseIntentAmount;
using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Tests.TestDoubles;

/// <summary>Stub síncrono para los handler tests. SEC-M1.C es verificable localmente; los tests del
/// handler no deben depender de que la firma sea válida — eso lo cubren los tests del verifier.</summary>
public sealed class FakePurchaseIntentAmountVerifier(Result? forcedResult = null) : IPurchaseIntentAmountVerifier
{
    public PurchaseIntentAmountInput? LastInput { get; private set; }
    public string? LastSignature { get; private set; }

    public Result Verify(PurchaseIntentAmountInput input, string? signature)
    {
        LastInput = input;
        LastSignature = signature;
        return forcedResult ?? Result.Success();
    }
}
