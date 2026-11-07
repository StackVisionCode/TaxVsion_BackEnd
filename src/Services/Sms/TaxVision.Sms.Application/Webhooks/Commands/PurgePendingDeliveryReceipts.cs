using BuildingBlocks.Results;
using TaxVision.Sms.Application.Abstractions;

namespace TaxVision.Sms.Application.Webhooks.Commands;

/// <summary>Borra los DLR que nunca encontraron su mensaje. Son avisos técnicos, no datos de negocio.</summary>
public sealed record PurgePendingDeliveryReceiptsCommand(int RetentionDays);

public static class PurgePendingDeliveryReceiptsHandler
{
    public static async Task<Result<int>> Handle(
        PurgePendingDeliveryReceiptsCommand command,
        IPendingDeliveryReceiptRepository receipts,
        CancellationToken ct
    ) =>
        Result.Success(
            await receipts.DeleteReceivedBeforeAsync(DateTime.UtcNow.AddDays(-Math.Max(1, command.RetentionDays)), ct)
        );
}
