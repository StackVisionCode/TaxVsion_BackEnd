using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Email.Sending;
using TaxVision.Notification.Application.Email.Sending.Commands;
using TaxVision.Notification.Domain.Emailing.Sending;
using Wolverine;

namespace TaxVision.Notification.Application.Email.Campaigns.Commands;

/// <summary>Envía un correo de prueba de la campaña a una dirección, reutilizando el envío por plantilla.</summary>
public sealed record SendCampaignTestCommand(
    Guid CampaignId,
    Guid TenantId,
    string ToEmail,
    Dictionary<string, string?>? Variables
);

public static class SendCampaignTestHandler
{
    public static async Task<Result<OutboundEmailResponse>> Handle(
        SendCampaignTestCommand command,
        IEmailCampaignRepository repository,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.ToEmail))
            return Result.Failure<OutboundEmailResponse>(new Error("Campaign.Recipients", "A test recipient is required."));

        var campaign = await repository.GetByIdAsync(command.CampaignId, command.TenantId, ct);
        if (campaign is null)
            return Result.Failure<OutboundEmailResponse>(new Error("Campaign.NotFound", "Campaign not found."));

        // Reutiliza el envío por plantilla (render + layout con el token del usuario del request).
        var send = new SendTemplateEmailCommand(
            command.TenantId,
            campaign.TemplateId,
            command.Variables ?? new Dictionary<string, string?>(),
            EmailPriority.Normal,
            [new EmailRecipientInput(command.ToEmail)],
            AttachmentFileIds: null,
            ApplyLayout: true
        );

        return await bus.InvokeAsync<Result<OutboundEmailResponse>>(send, ct);
    }
}
