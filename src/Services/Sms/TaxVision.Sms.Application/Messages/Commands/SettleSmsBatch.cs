using Microsoft.Extensions.Logging;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Messages.Abstractions;
using TaxVision.Sms.Domain.Messages;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Sms.Application.Messages.Commands;

/// <summary>Liquida la reserva del Wallet de un lote individual. Null en el bearer = M2M del tenant.</summary>
public sealed record SettleSmsBatchCommand(Guid TenantId, Guid BatchId, string? CallerBearerToken = null);

public sealed record SettleSmsBatchResponse(bool Settled, int ConsumedUnits);

public static class SettleSmsBatchHandler
{
    // La llamada al Wallet va fuera de cualquier transacción.
    [NonTransactional]
    public static Task<SettleSmsBatchResponse> Handle(
        SettleSmsBatchCommand command,
        ISmsMessageRepository messages,
        IWalletSpendClient wallet,
        ILogger<SettleSmsBatchCommand> logger,
        CancellationToken ct
    ) => SettleAsync(command.TenantId, command.BatchId, command.CallerBearerToken, messages, wallet, logger, ct);

    /// <summary>
    /// Cobra las filas del lote que el proveedor aceptó y libera el resto (D-SMS-3). Si queda alguna en curso o
    /// desconocida no liquida: la reserva sigue abierta hasta que se resuelva o venza en el Wallet.
    /// </summary>
    public static async Task<SettleSmsBatchResponse> SettleAsync(
        Guid tenantId,
        Guid batchId,
        string? callerBearerToken,
        ISmsMessageRepository messages,
        IWalletSpendClient wallet,
        ILogger logger,
        CancellationToken ct
    )
    {
        var rows = await messages.ListByBatchAsync(tenantId, batchId, ct);
        if (rows.Any(m => m.IsUnresolved))
        {
            logger.LogInformation(
                "SMS batch {BatchId}: {Unresolved} message(s) without provider outcome; wallet reservation left open.",
                batchId,
                rows.Count(m => m.IsUnresolved)
            );
            return new SettleSmsBatchResponse(false, 0);
        }

        // Solo las filas de este lote: una clave reusada de otro lote ya se cobró en su propia reserva.
        var consumed = rows.Count(m => m.Status is SmsMessageStatus.Accepted or SmsMessageStatus.Delivered);
        try
        {
            await wallet.SettleAsync(
                tenantId,
                WalletReferenceTypes.IndividualSms,
                batchId,
                consumed,
                callerBearerToken,
                ct
            );
            return new SettleSmsBatchResponse(true, consumed);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "SMS batch {BatchId}: wallet settle failed ({Consumed} consumed); reservation left open.",
                batchId,
                consumed
            );
            return new SettleSmsBatchResponse(false, consumed);
        }
    }
}
