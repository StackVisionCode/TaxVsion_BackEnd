using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Sending;
using Wolverine;

namespace TaxVision.Notification.Application.Email.Sending.Commands;

/// <summary>
/// Envío de un correo ad-hoc (cuerpo explícito). Persiste el mensaje (Queued) y encola la entrega
/// asíncrona por evento durable; NO envía dentro del request.
/// </summary>
public sealed record SendEmailCommand(
    Guid TenantId,
    string Subject,
    string HtmlBody,
    string? TextBody,
    EmailPriority Priority,
    IReadOnlyList<EmailRecipientInput> Recipients,
    IReadOnlyList<Guid>? AttachmentFileIds
);

public static class SendEmailHandler
{
    public static async Task<Result<OutboundEmailResponse>> Handle(
        SendEmailCommand command,
        IOutboundEmailRepository repository,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Result.Failure<OutboundEmailResponse>(new Error("Email.Tenant", "Tenant is required."));

        if (command.Recipients is null || command.Recipients.Count == 0)
            return Result.Failure<OutboundEmailResponse>(
                new Error("Email.Recipients", "At least one recipient is required.")
            );

        var recipients = command.Recipients.Select(r => (r.Address, r.Kind, r.Name)).ToList();
        var attachmentsJson = JsonSerializer.Serialize(command.AttachmentFileIds ?? []);

        var result = OutboundEmailMessage.Create(
            command.TenantId,
            command.Subject,
            command.HtmlBody,
            command.TextBody ?? string.Empty,
            command.Priority,
            recipients,
            attachmentsJson,
            templateId: null,
            templateVersionId: null,
            campaignId: null,
            correlationId: correlation.CorrelationId
        );
        if (result.IsFailure)
            return Result.Failure<OutboundEmailResponse>(result.Error);

        var message = result.Value;
        await repository.AddAsync(message, ct);
        await bus.PublishAsync(
            new EmailSendRequestedIntegrationEvent
            {
                MessageId = message.Id,
                TenantId = message.TenantId,
                CorrelationId = correlation.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(OutboundEmailMapper.ToResponse(message));
    }
}
