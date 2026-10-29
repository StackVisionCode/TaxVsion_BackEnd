using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using Wolverine;

namespace TaxVision.Notification.Infrastructure.Messaging;

internal sealed class ClientFormPushRetryScheduler(IMessageBus bus) : IClientFormPushRetryScheduler
{
    public async Task ScheduleAsync(RetryClientFormPushRequested request, TimeSpan delay) =>
        await bus.ScheduleAsync(request, delay);
}
