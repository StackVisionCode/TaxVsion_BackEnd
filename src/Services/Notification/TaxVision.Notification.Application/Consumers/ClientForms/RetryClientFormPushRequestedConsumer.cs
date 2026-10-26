using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class RetryClientFormPushRequestedConsumer
{
    public static Task Handle(
        RetryClientFormPushRequested request,
        ClientFormNotificationSender sender,
        CancellationToken ct
    ) => sender.RetryPushAsync(request, ct);
}
