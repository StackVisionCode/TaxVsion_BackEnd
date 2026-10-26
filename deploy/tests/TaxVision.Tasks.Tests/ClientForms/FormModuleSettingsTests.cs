using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormModuleSettingsTests
{
    [Fact]
    public void New_row_records_operator_and_starts_with_the_office_defaults()
    {
        var result = FormModuleSettings.Create(
            FormTemplateHandlerTestDoubles.TenantId,
            " Pilot ",
            FormTemplateHandlerTestDoubles.UserId
        );

        Assert.Equal(FormTemplateHandlerTestDoubles.UserId, result.Value.EnabledByUserId);
        Assert.Equal("Pilot", result.Value.Note);
        Assert.Equal(FormModuleCapabilities.Defaults, result.Value.Capabilities);
    }

    [Fact]
    public void Office_without_row_uses_the_defaults()
    {
        var capabilities = FormModuleCapabilities.Of(null);

        Assert.True(capabilities.ExternalParticipantsEnabled);
        Assert.True(capabilities.AutoCustomerCreationEnabled);
        Assert.False(capabilities.SignatureStepEnabled);
        Assert.False(capabilities.IntakeLinksEnabled);
        Assert.True(capabilities.PushNotificationsEnabled);
    }

    [Fact]
    public void Office_with_row_uses_its_own_flags()
    {
        var settings = FormModuleSettings
            .Create(FormTemplateHandlerTestDoubles.TenantId, null, FormTemplateHandlerTestDoubles.UserId)
            .Value;
        settings.SetCapabilities(false, true, false, true, true, FormTemplateHandlerTestDoubles.UserId);

        Assert.Equal(new FormModuleCapabilities(false, true, false, true, true), FormModuleCapabilities.Of(settings));
    }

    [Fact]
    public void Note_longer_than_the_limit_is_rejected()
    {
        var settings = FormModuleSettings
            .Create(FormTemplateHandlerTestDoubles.TenantId, null, FormTemplateHandlerTestDoubles.UserId)
            .Value;

        var result = settings.SetNote(new string('x', FormModuleSettings.NoteMaxLength + 1), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Null(settings.Note);
    }
}
