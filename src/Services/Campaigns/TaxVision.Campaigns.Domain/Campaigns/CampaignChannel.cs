namespace TaxVision.Campaigns.Domain.Campaigns;

/// <summary>
/// Canales que una campaña puede seleccionar. <see cref="System.FlagsAttribute"/>: una campaña
/// multicanal es la combinación (OR) — envío a <b>todos los canales seleccionados</b>
/// (<c>Domain_Design.md §7.5</c>). Se persiste como un solo <c>int</c>.
/// </summary>
[Flags]
public enum CampaignChannel
{
    None = 0,
    Email = 1,
    Sms = 2,
    WhatsApp = 4,
    Push = 8,
    InApp = 16,
}
