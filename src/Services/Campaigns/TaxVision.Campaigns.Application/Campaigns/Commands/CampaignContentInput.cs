using BuildingBlocks.Results;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Application.Campaigns.Commands;

/// <summary>Contenido de un canal enviado por el cliente (create/update). El canal llega como string
/// ("Email"/"Sms"/"Push"/"WhatsApp"/"InApp") y se parsea al flag del dominio.</summary>
public sealed record ChannelContentInput(string Channel, string? Subject, string? Title, string Body);

/// <summary>Aplica (upsert + limpieza) el contenido por canal sobre un <see cref="Campaign"/> en Draft,
/// reusado por create y update. Limpia el contenido de canales que ya no están seleccionados.</summary>
public static class CampaignContentApplier
{
    public static Result Apply(Campaign campaign, IReadOnlyList<ChannelContentInput>? contents)
    {
        if (contents is null)
            return Result.Success();

        // Purga contenido de canales que dejaron de estar seleccionados.
        foreach (var existing in campaign.Contents.ToList())
            if (!campaign.Channels.HasFlag(existing.Channel))
                campaign.ClearChannelContent(existing.Channel);

        foreach (var input in contents)
        {
            if (
                !Enum.TryParse<CampaignChannel>(input.Channel, ignoreCase: true, out var channel)
                || channel == CampaignChannel.None
            )
                return Result.Failure(CampaignErrors.ContentChannelInvalid);

            var set = campaign.SetChannelContent(channel, input.Subject, input.Title, input.Body);
            if (set.IsFailure)
                return set;
        }

        return Result.Success();
    }
}
