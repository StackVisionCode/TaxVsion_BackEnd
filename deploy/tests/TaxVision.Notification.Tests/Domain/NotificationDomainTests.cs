using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests.Domain;

public sealed class NotificationDomainTests
{
    [Fact]
    public void Notification_requires_tenant_and_recipient()
    {
        var missingTenant = NotificationLog.Create(
            Guid.Empty,
            NotificationChannel.Email,
            "user@example.com",
            "Subject",
            "template",
            null,
            null
        );
        var missingRecipient = NotificationLog.Create(
            Guid.NewGuid(),
            NotificationChannel.Email,
            " ",
            "Subject",
            "template",
            null,
            null
        );

        Assert.Equal("Notification.Tenant", missingTenant.Error.Code);
        Assert.Equal("Notification.Recipient", missingRecipient.Error.Code);
    }

    [Fact]
    public void Sent_notification_clears_previous_error()
    {
        var log = CreateLog();
        log.MarkFailed("temporary SMTP error");

        log.MarkSent();

        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Null(log.Error);
        Assert.NotNull(log.SentAtUtc);
    }

    [Fact]
    public void Failure_text_is_bounded()
    {
        var log = CreateLog();

        log.MarkFailed(new string('x', 700));

        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Equal(512, log.Error!.Length);
    }

    private static NotificationLog CreateLog() =>
        NotificationLog
            .Create(
                Guid.NewGuid(),
                NotificationChannel.Email,
                "user@example.com",
                "Subject",
                "template",
                Guid.NewGuid(),
                "correlation"
            )
            .Value;
}
