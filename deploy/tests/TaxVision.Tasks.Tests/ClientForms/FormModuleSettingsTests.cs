using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormModuleSettingsTests
{
    [Fact]
    public void Enabling_records_operator_and_time_with_future_flags_off()
    {
        var now = DateTime.UtcNow;

        var result = FormModuleSettings.Create(
            FormTemplateHandlerTestDoubles.TenantId,
            true,
            " Pilot ",
            FormTemplateHandlerTestDoubles.UserId,
            now
        );

        Assert.True(result.Value.Enabled);
        Assert.Equal(now, result.Value.EnabledAtUtc);
        Assert.Equal(FormTemplateHandlerTestDoubles.UserId, result.Value.EnabledByUserId);
        Assert.Equal("Pilot", result.Value.Note);
        Assert.False(result.Value.ExternalParticipantsEnabled);
        Assert.False(result.Value.IntakeLinksEnabled);
        Assert.False(result.Value.AutoCustomerCreationEnabled);
        Assert.False(result.Value.SignatureStepEnabled);
        Assert.False(result.Value.PushNotificationsEnabled);
    }

    [Fact]
    public void Disabling_clears_enabled_timestamp()
    {
        var settings = FormModuleSettings.Create(
            FormTemplateHandlerTestDoubles.TenantId,
            true,
            null,
            FormTemplateHandlerTestDoubles.UserId,
            DateTime.UtcNow
        ).Value;

        var result = settings.SetAvailability(false, null, FormTemplateHandlerTestDoubles.UserId, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.False(settings.Enabled);
        Assert.Null(settings.EnabledAtUtc);
    }
}
