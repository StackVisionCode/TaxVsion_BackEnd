using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Email.Templates;
using TaxVision.Notification.Domain.Emailing.Sending;
using TaxVision.Notification.Domain.Emailing.Templates;
using Wolverine;

namespace TaxVision.Notification.Application.Email.Sending.Commands;

/// <summary>
/// Envío por plantilla. Renderiza el asunto/cuerpo con Fluid (variables validadas) y aplica el layout
/// default DENTRO del request (con el token del usuario para leer de CloudStorage), guardando el cuerpo
/// final ya renderizado. La entrega SMTP es asíncrona por evento.
/// </summary>
public sealed record SendTemplateEmailCommand(
    Guid TenantId,
    Guid TemplateId,
    IReadOnlyDictionary<string, string?> Variables,
    EmailPriority Priority,
    IReadOnlyList<EmailRecipientInput> Recipients,
    IReadOnlyList<Guid>? AttachmentFileIds,
    bool ApplyLayout
);

public static class SendTemplateEmailHandler
{
    public static async Task<Result<OutboundEmailResponse>> Handle(
        SendTemplateEmailCommand command,
        IEmailTemplateRepository templates,
        ITemplateStorageService templateStorage,
        ITemplateRenderer renderer,
        IEmailLayoutRepository layouts,
        ILayoutStorageService layoutStorage,
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
            return Result.Failure<OutboundEmailResponse>(new Error("Email.Recipients", "At least one recipient is required."));

        var template = await templates.GetByIdAsync(command.TemplateId, command.TenantId, ct);
        if (template is null)
            return Result.Failure<OutboundEmailResponse>(new Error("EmailTemplate.NotFound", "Template not found."));

        if (template.Status != EmailTemplateStatus.Active || template.CurrentVersionId is null)
            return Result.Failure<OutboundEmailResponse>(
                new Error("EmailTemplate.NotPublished", "Template has no published version.")
            );

        var version = await templates.GetVersionAsync(template.Id, template.CurrentVersionId.Value, ct);
        if (version is null)
            return Result.Failure<OutboundEmailResponse>(new Error("EmailTemplate.NotFound", "Published version not found."));

        var htmlResult = await templateStorage.GetHtmlAsync(version.HtmlFileId, ct);
        if (htmlResult.IsFailure)
            return Result.Failure<OutboundEmailResponse>(htmlResult.Error);

        var rendered = renderer.Render(
            new RenderRequest(
                version.SubjectTemplate,
                htmlResult.Value,
                null,
                command.Variables,
                EmailTemplateMapper.ParseVariables(template.VariablesJson)
            )
        );
        if (rendered.IsFailure)
            return Result.Failure<OutboundEmailResponse>(rendered.Error);

        var finalHtml = rendered.Value.HtmlBody;
        if (command.ApplyLayout)
        {
            var layout = await layouts.GetDefaultAsync(command.TenantId, ct);
            if (layout?.HtmlFileId is { } layoutFileId)
            {
                var layoutHtml = await layoutStorage.GetHtmlAsync(layoutFileId, ct);
                if (layoutHtml.IsSuccess)
                {
                    var wrapped = renderer.ApplyLayout(layoutHtml.Value, rendered.Value.HtmlBody);
                    if (wrapped.IsSuccess)
                        finalHtml = wrapped.Value;
                }
            }
        }

        var recipients = command.Recipients.Select(r => (r.Address, r.Kind, r.Name)).ToList();
        var attachmentsJson = JsonSerializer.Serialize(command.AttachmentFileIds ?? []);

        var result = OutboundEmailMessage.Create(
            command.TenantId,
            rendered.Value.Subject,
            finalHtml,
            rendered.Value.TextBody,
            command.Priority,
            recipients,
            attachmentsJson,
            template.Id,
            version.Id,
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
