using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Emailing.Campaigns;

/// <summary>Destinatario explícito de una campaña, con variables propias para el render por-destinatario.</summary>
/// <remarks>Migration target: <b>Scribe</b>. See <c>Responsibility_Map.md</c>. Se elimina de Notification en Fase 7.</remarks>
public sealed class EmailCampaignRecipient : BaseEntity
{
    private EmailCampaignRecipient() { }

    public Guid CampaignId { get; private set; }
    public string Address { get; private set; } = default!;
    public string? Name { get; private set; }

    /// <summary>Variables específicas del destinatario (objeto JSON), fusionadas con las globales al renderizar.</summary>
    public string VariablesJson { get; private set; } = "{}";

    internal static EmailCampaignRecipient Create(string address, string? name, string? variablesJson) =>
        new()
        {
            Id = Guid.NewGuid(),
            Address = address.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            VariablesJson = string.IsNullOrWhiteSpace(variablesJson) ? "{}" : variablesJson,
        };
}
