using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Notifications.Queries;

/// <summary>Solo el estado: nunca destinatario, asunto ni contenido.</summary>
public sealed record NotificationDeliveryStatusResponse(string Status);

public sealed record GetNotificationDeliveryStatusQuery(Guid TenantId, Guid EventId);

public static class GetNotificationDeliveryStatusHandler
{
    public static readonly Error NotFound = new(
        "NotificationLog.NotFound",
        "No email of this office was found for the event."
    );

    public static async Task<Result<NotificationDeliveryStatusResponse>> Handle(
        GetNotificationDeliveryStatusQuery query,
        INotificationDeliveryStatusReader reader,
        CancellationToken ct
    ) =>
        await reader.GetEmailStatusByEventAsync(query.TenantId, query.EventId, ct) is { } status
            ? Result.Success(new NotificationDeliveryStatusResponse(status.ToString()))
            : Result.Failure<NotificationDeliveryStatusResponse>(NotFound);
}
