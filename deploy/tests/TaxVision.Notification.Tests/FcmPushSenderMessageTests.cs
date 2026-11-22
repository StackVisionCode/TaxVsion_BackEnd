using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Infrastructure.Push;

namespace TaxVision.Notification.Tests;

/// <summary>Armado del mensaje FCM: TTL explícito para Android y web; sin TTL, lo de siempre.</summary>
public sealed class FcmPushSenderMessageTests
{
    [Fact]
    public void Explicit_ttl_is_applied_to_android_and_web()
    {
        var android = FcmPushSender.BuildMessage(
            new PushMessage("t", PushPlatform.Fcm, "T", "B", TimeToLive: TimeSpan.FromSeconds(45))
        );
        var web = FcmPushSender.BuildMessage(
            new PushMessage("t", PushPlatform.Web, "T", "B", TimeToLive: TimeSpan.FromSeconds(45))
        );

        Assert.Equal(TimeSpan.FromSeconds(45), android.Android!.TimeToLive);
        Assert.Equal("45", web.Webpush!.Headers["TTL"]);
    }

    [Fact]
    public void Without_ttl_android_keeps_one_hour()
    {
        var android = FcmPushSender.BuildMessage(new PushMessage("t", PushPlatform.Fcm, "T", "B"));
        var web = FcmPushSender.BuildMessage(new PushMessage("t", PushPlatform.Web, "T", "B"));

        Assert.Equal(TimeSpan.FromHours(1), android.Android!.TimeToLive);
        Assert.False(web.Webpush?.Headers?.ContainsKey("TTL") ?? false);
    }
}
