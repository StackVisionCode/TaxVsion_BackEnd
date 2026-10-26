using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Configurations;

public sealed class FormModuleSettingsConfiguration : IEntityTypeConfiguration<FormModuleSettings>
{
    public void Configure(EntityTypeBuilder<FormModuleSettings> builder)
    {
        builder.ToTable("FormModuleSettings");
        builder.HasKey(settings => settings.TenantId);
        builder.Ignore(settings => settings.Id);
        builder.Property(settings => settings.Note).HasMaxLength(FormModuleSettings.NoteMaxLength);
        builder.Ignore(settings => settings.Capabilities);
        // Columna obsoleta: se mapea por nombre para no tocar el esquema.
        builder.Property<bool>("Enabled").IsRequired();
        builder.Property(settings => settings.ExternalParticipantsEnabled).IsRequired().HasDefaultValue(false);
        builder.Property(settings => settings.IntakeLinksEnabled).IsRequired().HasDefaultValue(false);
        builder.Property(settings => settings.AutoCustomerCreationEnabled).IsRequired().HasDefaultValue(false);
        builder.Property(settings => settings.SignatureStepEnabled).IsRequired().HasDefaultValue(false);
        builder.Property(settings => settings.PushNotificationsEnabled).IsRequired().HasDefaultValue(false);
    }
}
