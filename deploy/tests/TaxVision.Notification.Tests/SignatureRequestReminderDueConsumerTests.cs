using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Signature;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

public sealed class SignatureRequestReminderDueConsumerTests
{
    [Fact]
    public async Task Sms_leaves_an_audit_row()
    {
        var logs = new RecordingNotificationLogs();
        var sms = new CapturingSms();
        var evt = new SignatureRequestReminderDueIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            SignatureRequestId = Guid.NewGuid(),
            SignerId = Guid.NewGuid(),
            Email = "signer@customer.com",
            FullName = "Ada Lovelace",
            Language = "En",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            RemindersSent = 1,
            PublicToken = "secret-token",
            PhoneE164 = "+15550001111",
            PreferredChannel = "Sms",
        };

        await SignatureRequestReminderDueConsumer.Handle(
            evt,
            new TestDispatchers.UnusedEmailGateway(),
            new TestDispatchers.UnusedScribe(),
            new NotificationDispatcherHarness(smsSender: sms, logs: logs).Dispatcher,
            new AllowAllContactPolicyRepository(),
            Options.Create(new PortalOptions { BaseUrl = "https://app.test" }),
            TestPlatform.Settings,
            new FakeTenantHostResolver(),
            new FixedCorrelationContext(),
            NullLogger<SignatureRequestReminderDueIntegrationEvent>.Instance,
            CancellationToken.None
        );

        var log = Assert.Single(logs.Logs);
        Assert.Equal(NotificationChannel.Sms, log.Channel);
        Assert.Equal("sig.request_reminder_due.sms", log.Kind);
        Assert.Equal("+15550001111", log.Recipient);
        Assert.DoesNotContain("secret-token", log.Subject);
        Assert.Equal(evt.EventId.ToString("N"), Assert.Single(sms.Tokens));
    }
}
