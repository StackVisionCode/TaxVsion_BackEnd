using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Application.Runs.Commands;

public enum RunFundingOutcome
{
    Reserved = 1,
    InsufficientFunds = 2,
    WalletUnavailable = 3,
}

/// <summary>Lo que respondió el Wallet al reservar el run.</summary>
public sealed record RunFunding(RunFundingOutcome Outcome, long DeficitMicros = 0, string Currency = "USD")
{
    public static readonly RunFunding Reserved = new(RunFundingOutcome.Reserved);
    public static readonly RunFunding WalletUnavailable = new(RunFundingOutcome.WalletUnavailable);
}

/// <summary>Paso 2 del patrón BB-8: la reserva, sin transacción abierta y con el run ya persistido.</summary>
internal static class CampaignRunFunding
{
    /// <summary>El Id del run ya está guardado: repetir la llamada devuelve la misma reserva.</summary>
    public static async Task<RunFunding> ReserveAsync(
        IWalletSpendClient wallet,
        Guid tenantId,
        Guid runId,
        WalletUnitCounts units,
        string? callerBearerToken,
        CancellationToken ct
    )
    {
        if (units.BillableTotal <= 0)
            return RunFunding.Reserved;

        var auth = await wallet.ReserveAsync(
            tenantId,
            WalletReferenceTypes.CampaignRun,
            runId,
            units,
            callerBearerToken,
            ct
        );
        if (!auth.Reachable)
            return RunFunding.WalletUnavailable;
        return auth.Authorized
            ? RunFunding.Reserved
            : new RunFunding(RunFundingOutcome.InsufficientFunds, auth.DeficitMicros, auth.Currency);
    }

    /// <summary>
    /// Unidades cobrables por canal del run (Σ destinatarios×canal, sobre TODAS las unidades materializadas). Solo
    /// Email y SMS se cobran (<see cref="BillableChannels"/>): Push es del sistema (gratis) y WhatsApp está oculto. La
    /// liquidación al cierre libera la fracción Skipped (00_Plan §5).
    /// </summary>
    public static WalletUnitCounts CountBillableUnits(CampaignRun run)
    {
        long email = 0,
            sms = 0;
        foreach (var recipient in run.Recipients)
            switch (recipient.Channel)
            {
                case CampaignChannel.Email:
                    email++;
                    break;
                case CampaignChannel.Sms:
                    sms++;
                    break;
            }
        return new WalletUnitCounts(email, sms, 0, 0);
    }
}
