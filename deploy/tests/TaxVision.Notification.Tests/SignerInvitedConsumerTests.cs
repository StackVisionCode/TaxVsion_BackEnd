using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Signature;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

public sealed class SignerInvitedConsumerTests
{
    [Fact]
    public async Task Sms_leaves_an_audit_row()
    {
        var logs = new RecordingNotificationLogs();
        var sms = new CapturingSms();
        var evt = new SignerInvitedIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            SignatureRequestId = Guid.NewGuid(),
            SignerId = Guid.NewGuid(),
            Email = "signer@customer.com",
            FullName = "Ada Lovelace",
            Order = 1,
            Language = "En",
            PhoneE164 = "+15550001111",
            PreferredChannel = "Sms",
            PublicToken = "secret-token",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            RevocationEpoch = 0,
            RequiresConsent = false,
            RequiresSequentialSigning = false,
        };

        await SignerInvitedConsumer.Handle(
            evt,
            new TestDispatchers.UnusedScribe(),
            new NotificationDispatcherHarness(smsSender: sms, logs: logs).Dispatcher,
            NoPortalRecipientResolver.Instance,
            FixedRecipientLocale.English,
            Options.Create(new PortalOptions { BaseUrl = "https://app.test" }),
            TestPlatform.Settings,
            new FakeTenantHostResolver(),
            new FixedCorrelationContext(),
            NullLogger<SignerInvitedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        var log = Assert.Single(logs.Logs);
        Assert.Equal(NotificationChannel.Sms, log.Channel);
        Assert.Equal("sig.signer_invited.sms", log.Kind);
        Assert.Equal("+15550001111", log.Recipient);
        Assert.DoesNotContain("secret-token", log.Subject);
        // Mismo token de idempotencia que antes: el id del evento.
        Assert.Equal(evt.EventId.ToString("N"), Assert.Single(sms.Tokens));
    }
}
