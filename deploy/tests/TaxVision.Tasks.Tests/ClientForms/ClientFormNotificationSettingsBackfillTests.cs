using TaxVision.Tasks.ClientForms.Application.Settings;
using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ClientFormNotificationSettingsBackfillTests
{
    [Fact]
    public void Each_tenant_flag_is_sent_with_the_oldest_date_so_a_real_change_always_wins()
    {
        var withPush = Settings(pushEnabled: true);
        var withoutPush = Settings(pushEnabled: false);

        var commands = ClientFormNotificationSettingsBackfill.Commands([withPush, withoutPush], "corr");

        Assert.Equal(2, commands.Count);
        Assert.All(commands, command => Assert.Equal(DateTime.UnixEpoch, command.OccurredOn));
        Assert.All(commands, command => Assert.Equal(command.EventId, command.CommandId));
        Assert.True(commands.Single(command => command.TenantId == withPush.TenantId).PushNotificationsEnabled);
        Assert.False(commands.Single(command => command.TenantId == withoutPush.TenantId).PushNotificationsEnabled);
    }

    private static FormModuleSettings Settings(bool pushEnabled)
    {
        var userId = Guid.NewGuid();
        var settings = FormModuleSettings.Create(Guid.NewGuid(), null, userId).Value;
        settings.SetCapabilities(false, false, false, false, pushEnabled, userId);
        return settings;
    }
}
