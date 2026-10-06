namespace TaxVision.Campaigns.Domain.Campaigns;

/// <summary>
/// Contenido de una campaña para UN canal (override multicanal). Entidad hija de <see cref="Campaign"/> —
/// se crea/muta solo desde el aggregate. Cada canal usa los campos que le aplican:
/// <list type="bullet">
///   <item><b>Email</b>: <see cref="Subject"/> + <see cref="Body"/> (HTML/texto).</item>
///   <item><b>Sms</b>: <see cref="Body"/> (texto corto).</item>
///   <item><b>Push</b>: <see cref="Title"/> + <see cref="Body"/>.</item>
///   <item><b>WhatsApp/InApp</b>: <see cref="Body"/> (+ <see cref="Title"/>/<see cref="Subject"/> si aplica).</item>
/// </list>
/// Lleva <c>TenantId</c> como columna pero NO es <c>ITenantOwned</c> (se carga vía el padre, igual que
/// <see cref="CampaignSenderSelection"/>). Si un canal seleccionado no tiene contenido propio, el dispatch
/// cae al <c>Message</c>/<c>Subject</c> base de la campaña (compatibilidad).
/// </summary>
public sealed class CampaignContent
{
    public Guid Id { get; }
    public Guid CampaignId { get; }
    public Guid TenantId { get; }
    public CampaignChannel Channel { get; private set; }

    /// <summary>Asunto (Email). Ignorado en SMS/Push.</summary>
    public string? Subject { get; private set; }

    /// <summary>Título (Push). Ignorado en Email/SMS.</summary>
    public string? Title { get; private set; }

    /// <summary>Cuerpo del mensaje para este canal (obligatorio).</summary>
    public string Body { get; private set; } = default!;

    private CampaignContent() { }

    internal CampaignContent(
        Guid campaignId,
        Guid tenantId,
        CampaignChannel channel,
        string? subject,
        string? title,
        string body
    )
    {
        Id = Guid.NewGuid();
        CampaignId = campaignId;
        TenantId = tenantId;
        Channel = channel;
        Subject = subject;
        Title = title;
        Body = body;
    }

    internal void Update(string? subject, string? title, string body)
    {
        Subject = subject;
        Title = title;
        Body = body;
    }
}
