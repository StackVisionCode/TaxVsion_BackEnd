using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Activity.Abstractions;
using TaxVision.Wallet.Application.Wallet;
using TaxVision.Wallet.Domain.Pricing;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Application.Activity.Queries;

/// <summary>Detalle de un asiento: origen, canales con su tarifa congelada y los demás pasos de la misma referencia.</summary>
public sealed record GetTransactionQuery(Guid TenantId, Guid EntryId);

public static class GetTransactionHandler
{
    public static async Task<Result<TransactionDetailView>> Handle(
        GetTransactionQuery query,
        IWalletActivityReader reader,
        CancellationToken ct
    )
    {
        var row = await reader.GetAsync(query.TenantId, query.EntryId, ct);
        if (row is null)
            return Result.Failure<TransactionDetailView>(WalletActivityErrors.TransactionNotFound);

        IReadOnlyList<LedgerEntry> steps = row.Entry.ReferenceId is { } referenceId
            ? await reader.ListByReferenceAsync(query.TenantId, row.Entry.ReferenceType, referenceId, ct)
            : [row.Entry];
        IReadOnlyDictionary<PriceChannel, long>? prices = null;
        if (row.Reservation is { } reservation)
            prices = (await reader.GetUnitPricesAsync([reservation.PriceBookVersion], ct)).GetValueOrDefault(
                reservation.PriceBookVersion
            );

        return Result.Success(TransactionDetails.Build(row, steps, prices));
    }
}

internal static class TransactionDetails
{
    public static TransactionDetailView Build(
        LedgerEntryRow row,
        IReadOnlyList<LedgerEntry> steps,
        IReadOnlyDictionary<PriceChannel, long>? prices
    )
    {
        var entry = LedgerEntryView.From(row);
        var reservation = row.Reservation;
        var channels = (reservation?.ChannelUnits() ?? [])
            .Select(c =>
            {
                var unitPrice = prices?.GetValueOrDefault(c.Channel) ?? 0;
                return new ChannelChargeView(c.Channel.ToString(), c.Units, unitPrice, c.Units * unitPrice);
            })
            .ToList();
        var topUp = row.TopUp is { } t
            ? new TopUpDetailView(t.Provider, t.PaymentLabel, t.Automatic, t.Status.ToString())
            : null;

        return new TransactionDetailView(
            entry,
            row.Entry.ReferenceType,
            row.Entry.ReferenceId,
            entry.Description,
            entry.RequestedByUserId,
            reservation?.PriceBookVersion,
            channels,
            reservation?.ReservedUnits,
            reservation?.ConsumedUnits,
            steps.Select(Step).ToList(),
            topUp
        );
    }

    private static TransactionStepView Step(LedgerEntry e) =>
        new(
            e.Id,
            e.Movement.ToString(),
            e.Movement is MovementType.Reserve or MovementType.Release
                ? Math.Abs(e.DeltaHeldMicros)
                : Math.Abs(e.DeltaPostedMicros),
            e.CreatedAtUtc
        );
}
