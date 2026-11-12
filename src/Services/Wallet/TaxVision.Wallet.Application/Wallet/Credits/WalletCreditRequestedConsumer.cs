using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Wallet.Application.Wallet.Abstractions;

namespace TaxVision.Wallet.Application.Wallet.Credits;

/// <summary>
/// Un servicio pidió acreditar o revertir dinero por evento (sin M2M). Se aplica con los mismos casos de uso que antes
/// exponía el endpoint interno y se responde con <see cref="WalletCreditAppliedIntegrationEvent"/> sobre la MISMA
/// referencia opaca. Un fallo de negocio se responde con su código (no se reintenta); una excepción (base caída, etc.)
/// sube para que Wolverine reintente: el Wallet es idempotente por referencia, así que reintentar es seguro.
/// </summary>
public static class WalletCreditRequestedConsumer
{
    public static async Task<WalletCreditAppliedIntegrationEvent> Handle(
        WalletCreditRequestedIntegrationEvent evt,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
        ILogger<CreditWalletCommand> creditLogger,
        ILogger<ReverseWalletCreditCommand> reverseLogger,
        CancellationToken ct
    )
    {
        var result = evt.Reverse
            ? await ReverseWalletCreditHandler.Handle(
                new ReverseWalletCreditCommand(
                    evt.TenantId,
                    evt.ReferenceType,
                    evt.ReferenceId,
                    evt.AmountMicros,
                    evt.Currency,
                    evt.Reason
                ),
                wallets,
                unitOfWork,
                reverseLogger,
                ct
            )
            : await CreditWalletHandler.Handle(
                new CreditWalletCommand(
                    evt.TenantId,
                    evt.ReferenceType,
                    evt.ReferenceId,
                    evt.AmountMicros,
                    evt.Currency,
                    evt.Reason
                ),
                wallets,
                unitOfWork,
                creditLogger,
                ct
            );

        return new WalletCreditAppliedIntegrationEvent
        {
            // El resultado viaja con el tenant beneficiario, igual que la solicitud.
            TenantId = evt.TenantId,
            CorrelationId = evt.CorrelationId,
            ReferenceType = evt.ReferenceType,
            ReferenceId = evt.ReferenceId,
            Reverse = evt.Reverse,
            Succeeded = result.IsSuccess,
            LedgerEntryId = result.IsSuccess ? result.Value.LedgerEntryId : null,
            ErrorCode = result.IsFailure ? result.Error.Code : null,
            WasReplay = result.IsSuccess && result.Value.WasReplay,
        };
    }
}
