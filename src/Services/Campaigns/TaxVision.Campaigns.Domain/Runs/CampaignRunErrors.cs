using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain.Runs;

public static class CampaignRunErrors
{
    public static readonly Error TenantRequired = new("CampaignRun.Tenant", "TenantId is required.");
    public static readonly Error CampaignRequired = new("CampaignRun.Campaign", "CampaignId is required.");
    public static readonly Error NoRecipients = new(
        "CampaignRun.NoRecipients",
        "At least one recipient unit is required."
    );
    public static readonly Error NotFound = new("CampaignRun.NotFound", "Campaign run not found.");
    public static readonly Error RecipientNotFound = new("CampaignRun.RecipientNotFound", "Dispatch unit not found.");
    public static readonly Error NotDispatching = new("CampaignRun.NotDispatching", "The run is not dispatching.");
    public static readonly Error AlreadyDispatched = new(
        "CampaignRun.AlreadyDispatched",
        "The run already dispatched units and cannot be rejected."
    );

    // PEP money-OUT (F4): el envío se bloquea porque no alcanza el saldo del monedero. Lleva el faltante
    // (micros + dólares) para que el front muestre "faltan $X". Mapeado a 402 Payment Required.
    public static Error InsufficientFunds(long deficitMicros, string currency)
    {
        var dollars = deficitMicros / 1_000_000m;
        return new Error(
            "CampaignRun.InsufficientFunds",
            $"Saldo insuficiente para enviar: faltan {dollars:0.######} {currency} ({deficitMicros} micros)."
        );
    }

    // El Wallet no respondió al autorizar el cobro: fail-closed, el envío no sale. Transitorio (503).
    public static readonly Error WalletUnavailable = new(
        "CampaignRun.WalletUnavailable",
        "No se pudo autorizar el cobro del monedero; intentá de nuevo en unos segundos."
    );
}
