using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Emailing.Configurations;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class EmailProviderConfigurationConfiguration : IEntityTypeConfiguration<EmailProviderConfiguration>
{
    public void Configure(EntityTypeBuilder<EmailProviderConfiguration> builder)
    {
        builder.ToTable("EmailProviderConfigurations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(c => c.ProviderType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(c => c.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.FromEmail).HasMaxLength(320).IsRequired();
        builder.Property(c => c.FromName).HasMaxLength(128);

        builder.Property(c => c.Host).HasMaxLength(255);
        builder.Property(c => c.Username).HasMaxLength(320);
        builder.Property(c => c.ClientId).HasMaxLength(255);
        builder.Property(c => c.TenantProviderId).HasMaxLength(128);

        // Secretos cifrados (base64 de AES-GCM). Se dimensionan con holgura para tokens/keys largos.
        builder.Property(c => c.PasswordCipher).HasMaxLength(2048);
        builder.Property(c => c.ApiKeyCipher).HasMaxLength(2048);
        builder.Property(c => c.ClientSecretCipher).HasMaxLength(2048);

        builder.Property(c => c.CreatedAtUtc).IsRequired();

        // Invariante forzada por la BD: a lo sumo una configuración default por (Scope, TenantId).
        // El filtro IsDefault=1 hace que SQL Server rechace un segundo default (SqlException 2601/2627
        // → ConflictException/409) y sirve como índice de lookup del default en la resolución de envío.
        // Para System, TenantId es NULL y SQL Server trata dos NULL como duplicados en un índice único,
        // por lo que solo puede existir un default global.
        builder.HasIndex(c => new { c.Scope, c.TenantId }).HasFilter("[IsDefault] = 1").IsUnique();

        // Lookup para el listado por tenant/scope.
        builder.HasIndex(c => new { c.TenantId, c.Scope });
    }
}
