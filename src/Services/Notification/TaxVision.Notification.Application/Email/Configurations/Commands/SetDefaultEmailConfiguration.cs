using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Configurations;

namespace TaxVision.Notification.Application.Email.Configurations.Commands;

/// <summary>Marca una configuración como default de su scope (quitándoselo a las demás del mismo scope).</summary>
public sealed record SetDefaultEmailConfigurationCommand(Guid ConfigurationId, Guid? TenantId, bool IsPlatformAdmin);

public static class SetDefaultEmailConfigurationHandler
{
    public static async Task<Result> Handle(
        SetDefaultEmailConfigurationCommand command,
        IEmailProviderConfigurationRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var config = await repository.GetByIdAsync(command.ConfigurationId, command.TenantId, ct);
        if (config is null)
            return Result.Failure(new Error("EmailConfiguration.NotFound", "Configuration not found."));

        if (config.Scope == ProviderScope.System && !command.IsPlatformAdmin)
            return Result.Failure(
                new Error("EmailConfiguration.Forbidden", "Only platform administrators can manage global configurations.")
            );

        if (!config.IsActive)
            return Result.Failure(
                new Error("EmailConfiguration.Conflict", "An inactive configuration cannot be set as default.")
            );

        await repository.ClearDefaultsAsync(config.Scope, config.TenantId, ct);
        config.SetAsDefault();
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
