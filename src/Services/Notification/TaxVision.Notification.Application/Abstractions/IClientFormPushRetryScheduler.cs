using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Abstractions;

public interface IClientFormPushRetryScheduler
{
    Task ScheduleAsync(RetryClientFormPushRequested request, TimeSpan delay);
}
