using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.Lifecycle;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>
/// El gateway real con su gate de entrega (inactivo, preferencia, catálogo). <see cref="Queued"/> son los
/// correos que salieron y <see cref="Suppressed"/> los que el gate calló.
/// </summary>
internal sealed class GatedEmailGateway(
    IUserNotificationPreferenceRepository preferences,
    IUserEmailDirectoryRepository? directory = null
) : IEmailDispatchGateway
{
    private readonly EventBasedEmailDispatchGateway _inner = new(
        new CapturingIntegrationEventPublisher(),
        new RecordingNotificationLogs(),
        new CountingUnitOfWork(),
        TestDeliveryGates.For(preferences, directory ?? new InMemoryUserEmailDirectoryRepository()),
        NullLogger<EventBasedEmailDispatchGateway>.Instance
    );

    public List<EmailDispatchRequest> Queued { get; } = [];
    public List<EmailDispatchRequest> Suppressed { get; } = [];

    public async Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
    {
        var result = await _inner.QueueEmailAsync(request, ct);
        (result.Status == NotificationDispatchAttemptStatus.Suppressed ? Suppressed : Queued).Add(request);
        return result;
    }
}
