using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Infrastructure.Push;

namespace TaxVision.Notification.Tests;

// Solo el camino sin credencial: el estado de Firebase es estático y no se prueba el envío real.
public sealed class FcmPushSenderTests
{
    [Fact]
    public async Task Send_without_credentials_fails_as_not_configured()
    {
        var sender = new FcmPushSender(
            Options.Create(new FcmOptions { ServiceAccountJsonPath = MissingJsonPath }),
            NullLogger<FcmPushSender>.Instance
        );

        var result = await sender.SendAsync(new PushMessage("device-token", PushPlatform.Fcm, "Title", "Body"));

        Assert.True(result.IsFailure);
        Assert.Equal(PushErrorCodes.NotConfigured, result.Error.Code);
        Assert.False(PushErrorCodes.IsTransient(result.Error.Code));
    }

    [Fact]
    public async Task The_logging_sender_fails_as_not_configured()
    {
        var sender = new LoggingPushSender(NullLogger<LoggingPushSender>.Instance);

        var result = await sender.SendAsync(new PushMessage("device-token", PushPlatform.Fcm, "Title", "Body"));

        Assert.True(result.IsFailure);
        Assert.Equal(PushErrorCodes.NotConfigured, result.Error.Code);
    }

    [Theory]
    [InlineData("true", HealthStatus.Degraded)]
    [InlineData("false", HealthStatus.Healthy)]
    public async Task The_health_check_is_degraded_only_when_push_is_on_without_credentials(
        string useFcmPush,
        HealthStatus expected
    )
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notification:UseFcmPush"] = useFcmPush })
            .Build();
        var sender = new FcmPushSender(
            Options.Create(new FcmOptions { ServiceAccountJsonPath = MissingJsonPath }),
            NullLogger<FcmPushSender>.Instance
        );

        var result = await new FcmPushHealthCheck(configuration, sender).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(expected, result.Status);
    }

    private static readonly string MissingJsonPath = Path.Combine(
        Path.GetTempPath(),
        $"missing-firebase-{Guid.NewGuid():N}.json"
    );
}
