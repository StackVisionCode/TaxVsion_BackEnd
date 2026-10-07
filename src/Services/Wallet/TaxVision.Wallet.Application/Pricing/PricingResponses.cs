using TaxVision.Wallet.Domain.Pricing;

namespace TaxVision.Wallet.Application.Pricing;

/// <summary>Conteo de unidades por canal — entrada de la estimación (lo calcula preview-audience en Campaigns).</summary>
public sealed record PerChannelUnits(long Email = 0, long Sms = 0, long Push = 0, long WhatsApp = 0)
{
    public IEnumerable<(PriceChannel Channel, long Units)> NonZero()
    {
        if (Email > 0)
            yield return (PriceChannel.Email, Email);
        if (Sms > 0)
            yield return (PriceChannel.Sms, Sms);
        if (Push > 0)
            yield return (PriceChannel.Push, Push);
        if (WhatsApp > 0)
            yield return (PriceChannel.WhatsApp, WhatsApp);
    }
}

/// <summary>Tarifa vigente de un canal — item de <c>GET /wallet/rates</c>.</summary>
public sealed record RateView(string Channel, long UnitPriceMicros);

/// <summary>Catálogo vigente.</summary>
public sealed record RatesView(int Version, DateTime EffectiveFromUtc, IReadOnlyList<RateView> Rates);

/// <summary>Línea de una estimación.</summary>
public sealed record EstimateLine(string Channel, long Units, long UnitPriceMicros, long SubtotalMicros);

/// <summary>Resultado de <c>POST /wallet/estimate</c> — costo vs saldo, sin efectos.</summary>
public sealed record EstimateView(
    int PriceBookVersion,
    long CostMicros,
    long AvailableMicros,
    bool Sufficient,
    long DeficitMicros,
    string Currency,
    IReadOnlyList<EstimateLine> PerChannel
);
