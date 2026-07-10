using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Configurations;

namespace TaxVision.Notification.Application.Email.Configurations.Commands;

/// <summary>Envía un correo de prueba usando una configuración concreta para validar conectividad.</summary>
public sealed record TestEmailConfigurationCommand(
    Guid ConfigurationId,
    Guid? TenantId,
    bool IsPlatformAdmin,
    string ToEmail
);

public static class TestEmailConfigurationHandler
{
    public static async Task<Result> Handle(
        TestEmailConfigurationCommand command,
        IEmailConfigurationResolver resolver,
        ISmtpSendClient smtp,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.ToEmail))
            return Result.Failure(new Error("EmailConfiguration.Recipient", "A recipient is required for the test."));

        var config = await resolver.ResolveByIdAsync(command.ConfigurationId, command.TenantId, ct);
        if (config is null)
            return Result.Failure(new Error("EmailConfiguration.NotFound", "Configuration not found."));

        // Mismo guard que Update/SetDefault: probar una configuración global (System) usa las
        // credenciales del SaaS, así que solo lo permite un PlatformAdmin.
        if (config.Scope == ProviderScope.System && !command.IsPlatformAdmin)
            return Result.Failure(
                new Error(
                    "EmailConfiguration.Forbidden",
                    "Only platform administrators can test global configurations."
                )
            );

        // Los proveedores por API (Gmail/Graph/SendGrid…) se probarán vía sus adaptadores en fases
        // posteriores; por ahora el test cubre SMTP, que es de conectividad directa.
        if (config.ProviderType != EmailProviderType.Smtp)
            return Result.Failure(
                new Error(
                    "EmailConfiguration.TestUnsupported",
                    "Test send is currently supported only for SMTP configurations."
                )
            );

        if (string.IsNullOrWhiteSpace(config.Host))
            return Result.Failure(new Error("EmailConfiguration.Host", "SMTP host is not configured."));

        var message = new EmailMessage(
            command.ToEmail,
            "TaxVision - email configuration test",
            "<p>This is a test message from the TaxVision Notification service. If you received it, the SMTP configuration works.</p>",
            "This is a test message from the TaxVision Notification service. If you received it, the SMTP configuration works."
        );

        var connection = new SmtpConnection(
            config.Host!,
            config.Port ?? 587,
            config.Username,
            config.Password,
            config.UseSsl,
            config.FromEmail,
            config.FromName
        );

        return await smtp.SendAsync(connection, message, ct);
    }
}
