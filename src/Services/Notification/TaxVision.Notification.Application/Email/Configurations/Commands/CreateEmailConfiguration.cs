using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Configurations;

namespace TaxVision.Notification.Application.Email.Configurations.Commands;

/// <summary>
/// Crea una configuración de proveedor. Los secretos llegan en claro y se cifran aquí.
/// <c>IsPlatformAdmin</c> y <c>TenantId</c> los fija el controller desde el JWT, nunca el cliente.
/// </summary>
public sealed record CreateEmailConfigurationCommand(
    ProviderScope Scope,
    Guid? TenantId,
    bool IsPlatformAdmin,
    EmailProviderType ProviderType,
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
    string? TenantProviderId,
    bool IsDefault
);

public static class CreateEmailConfigurationHandler
{
    public static async Task<Result<EmailConfigurationResponse>> Handle(
        CreateEmailConfigurationCommand command,
        IEmailProviderConfigurationRepository repository,
        ISecretProtector protector,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.Scope == ProviderScope.System && !command.IsPlatformAdmin)
            return Result.Failure<EmailConfigurationResponse>(
                new Error("EmailConfiguration.Forbidden", "Only platform administrators can manage global configurations.")
            );

        if (command.Scope == ProviderScope.Tenant && (command.TenantId is null || command.TenantId == Guid.Empty))
            return Result.Failure<EmailConfigurationResponse>(
                new Error("EmailConfiguration.Tenant", "A tenant context is required for tenant configurations.")
            );

        var result = EmailProviderConfiguration.Create(
            command.Scope,
            command.TenantId,
            command.ProviderType,
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
            command.TenantProviderId,
            command.IsDefault
        );

        if (result.IsFailure)
            return Result.Failure<EmailConfigurationResponse>(result.Error);

        var config = result.Value;
        if (config.IsDefault)
            await repository.ClearDefaultsAsync(config.Scope, config.TenantId, ct);

        await repository.AddAsync(config, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(EmailConfigurationMapper.ToResponse(config));
    }

    private static string? Encrypt(ISecretProtector protector, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : protector.Protect(value);
}
