using TaxVision.Wallet.Application.Wallet.Consumers;
using Wolverine;
using Wolverine.ErrorHandling;

namespace TaxVision.Wallet.Api;

/// <summary>Reglas de fallo propias de Wallet. Van antes de ApplyStandardFailurePolicies en Program.cs.</summary>
public static class WalletFailurePolicies
{
    public static WolverineOptions ApplyWalletFailureRules(this WolverineOptions options)
    {
        // Pago sin orden es permanente: a la DLQ sin reintentos.
        options.Policies.OnExceptionOfType(typeof(WalletTopUpNotFoundException)).MoveToErrorQueue();
        return options;
    }
}
