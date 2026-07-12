using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Configurations;

namespace TaxVision.Notification.Application.Email.Configurations.Commands;

/// <summary>
/// Actualiza una configuración. Un secreto en blanco/nulo conserva el valor cifrado actual
/// (permite editar sin reenviar la contraseña/API key).
/// </summary>
public sealed record UpdateEmailConfigurationCommand(
    Guid ConfigurationId,
    Guid? TenantId,
    bool IsPlatformAdmin,
    string DisplayName,
    string FromEmail,
    string? FromName,
    string? Host,
    int? Port,
    string? Username,
    string? Password,
    bool UseSsl,
    string? ApiKey,
    string? ClientId,
    string? ClientSecret,
    string? TenantProviderId
);

public static class UpdateEmailConfigurationHandler
{
    public static async Task<Result<EmailConfigurationResponse>> Handle(
        UpdateEmailConfigurationCommand command,
        IEmailProviderConfigurationRepository repository,
        ISecretProtector protector,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var config = await repository.GetByIdAsync(command.ConfigurationId, command.TenantId, ct);
        if (config is null)
            return Result.Failure<EmailConfigurationResponse>(
                new Error("EmailConfiguration.NotFound", "Configuration not found.")
            );

        if (config.Scope == ProviderScope.System && !command.IsPlatformAdmin)
            return Result.Failure<EmailConfigurationResponse>(
                new Error(
                    "EmailConfiguration.Forbidden",
                    "Only platform administrators can manage global configurations."
                )
            );

        var result = config.Update(
            command.DisplayName,
            command.FromEmail,
            command.FromName,
            command.Host,
            command.Port,
            command.Username,
            Encrypt(protector, command.Password),
            command.UseSsl,
            Encrypt(protector, command.ApiKey),
            command.ClientId,
            Encrypt(protector, command.ClientSecret),
            command.TenantProviderId
        );

        if (result.IsFailure)
            return Result.Failure<EmailConfigurationResponse>(result.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailConfigurationMapper.ToResponse(config));
    }

    private static string? Encrypt(ISecretProtector protector, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : protector.Protect(value);
}
