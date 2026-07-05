using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Email.Sending.Commands;

public enum EmailTrackingEventType
{
    Delivered,
    Opened,
    Clicked,
    Bounced,
}

/// <summary>
/// Aplica un evento de tracking de proveedor (webhook) a un correo saliente y, si pertenece a una
/// campaña, actualiza sus contadores de aperturas/clics (solo la primera vez).
/// </summary>
public sealed record ApplyEmailTrackingEventCommand(Guid MessageId, EmailTrackingEventType Type, string? Detail);

public static class ApplyEmailTrackingEventHandler
{
    public static async Task<Result> Handle(
        ApplyEmailTrackingEventCommand command,
        IOutboundEmailRepository outbound,
        IEmailCampaignRepository campaigns,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var message = await outbound.GetForDeliveryAsync(command.MessageId, ct);
        if (message is null)
            return Result.Failure(new Error("EmailMessage.NotFound", "Message not found."));

        var firstOpen = false;
        var firstClick = false;
        switch (command.Type)
        {
            case EmailTrackingEventType.Delivered:
                message.MarkDelivered();
                break;
            case EmailTrackingEventType.Opened:
                firstOpen = message.MarkOpened();
                break;
            case EmailTrackingEventType.Clicked:
                firstClick = message.MarkClicked();
                break;
            case EmailTrackingEventType.Bounced:
                message.MarkBounced(command.Detail);
                break;
        }

        if (message.CampaignId is { } campaignId && (firstOpen || firstClick))
        {
            var campaign = await campaigns.GetForProcessingAsync(campaignId, ct);
            if (campaign is not null)
            {
                if (firstOpen)
                    campaign.IncrementOpened();
                if (firstClick)
                    campaign.IncrementClicked();
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
