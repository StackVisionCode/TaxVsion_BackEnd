using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record SendFormRequestReminderCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid RequestId,
    DateTime NowUtc
);

public static class SendFormRequestReminderHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        SendFormRequestReminderCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IOptions<ClientFormsOptions> options,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var found = await LoadAsync(command, requests, visibility, ct);
        if (found.IsFailure)
            return Result.Failure<FormRequestResponse>(found.Error);
        // Mismo tope que el automático, contando los manuales. Si pasó, 409.
        var recorded = found.Value.RecordReminder(options.Value.Reminders.MaxReminders, command.NowUtc);
        if (recorded.IsFailure)
            return Result.Failure<FormRequestResponse>(recorded.Error);
        var saved = await ReviewSupport.Save(
            recorded,
            found.Value,
            "request.reminder_sent",
            command.ByUserId,
            audit,
            unitOfWork,
            ct
        );
        // Guardar antes de publicar (T-8): un conflicto no manda un recordatorio que no quedó contado.
        if (saved.IsSuccess)
            await PublishAsync(found.Value, command, bus, correlation);
        return saved;
    }

    private static Task<Result<FormRequest>> LoadAsync(
        SendFormRequestReminderCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        CancellationToken ct
    ) =>
        ReviewSupport.LoadVisible(
            command.TenantId,
            command.ByUserId,
            command.CanViewAll,
            command.RequestId,
            requests,
            visibility,
            ct
        );

    private static ValueTask PublishAsync(
        FormRequest request,
        SendFormRequestReminderCommand command,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var isOverdue = request.DueAtUtc is { } due && command.NowUtc > due;
        return bus.PublishAsync(
            FormRequestIntegrationEventFactory.ReminderDue(
                request,
                isOverdue,
                isManual: true,
                command.ByUserId,
                correlation.CorrelationId
            )
        );
    }
}
