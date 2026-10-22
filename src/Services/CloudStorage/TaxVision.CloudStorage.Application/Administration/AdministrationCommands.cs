using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Providers;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Administration;

/// <summary>
/// Fase C3 — habilita/deshabilita links de compartir con Visibility.Public para el
/// tenant. Deshabilitado por defecto (datos fiscales); solo un actor con
/// cloudstorage.settings.manage puede cambiarlo (ver StorageAdministrationController).
/// </summary>
public sealed record SetPublicSharingPolicyCommand(Guid TenantId, bool Allow);

public sealed record SetStorageProviderCommand(
    Guid TenantId,
    string Mode,
    string ProviderId,
    string? BackupFrequency,
    string? BackupTime,
    string? BackupTimeZoneId
);

public static class SetStorageProviderHandler
{
    public static async Task<Result> Handle(
        SetStorageProviderCommand command,
        IStoragePolicyRepository policies,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var mode = command.Mode.Trim().ToLowerInvariant();
        var providerId = command.ProviderId.Trim().ToLowerInvariant();
        if (mode is not ("crm" or "primary" or "both" or "backup"))
            return Result.Failure(
                new BuildingBlocks.Results.Error(
                    "StoragePolicy.InvalidMode",
                    "Choose CRM storage, cloud storage, both, or scheduled backups."
                )
            );
        if (
            providerId.Length is < 2 or > 64
            || providerId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')
        )
            return Result.Failure(
                new BuildingBlocks.Results.Error("StoragePolicy.InvalidProvider", "Choose a valid cloud provider.")
            );

        string? frequency = null;
        string? time = null;
        string? timeZoneId = null;
        if (mode == "backup")
        {
            frequency = command.BackupFrequency?.Trim().ToLowerInvariant();
            time = command.BackupTime?.Trim();
            timeZoneId = command.BackupTimeZoneId?.Trim();
            if (
                frequency is not ("daily" or "weekly" or "monthly")
                || !TimeOnly.TryParseExact(
                    time,
                    "HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out _
                )
                || string.IsNullOrWhiteSpace(timeZoneId)
            )
                return Result.Failure(
                    new BuildingBlocks.Results.Error(
                        "StoragePolicy.InvalidBackupSchedule",
                        "Choose a backup frequency, local time, and time zone."
                    )
                );
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                return Result.Failure(
                    new BuildingBlocks.Results.Error(
                        "StoragePolicy.InvalidTimeZone",
                        "The selected time zone is not supported."
                    )
                );
            }
            catch (InvalidTimeZoneException)
            {
                return Result.Failure(
                    new BuildingBlocks.Results.Error(
                        "StoragePolicy.InvalidTimeZone",
                        "The selected time zone is not supported."
                    )
                );
            }
        }
        var policy = await policies.GetAsync(command.TenantId, ct);
        if (policy is null)
        {
            policy = TenantStoragePolicy.Create(command.TenantId, providerId);
            policies.Add(policy);
        }
        policy.Configure(providerId, mode, frequency, time, timeZoneId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class SetPublicSharingPolicyHandler
{
    public static async Task<Result> Handle(
        SetPublicSharingPolicyCommand command,
        IStorageLimitRepository limits,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var limit = await limits.GetAsync(command.TenantId, ct);
        if (limit is null)
            return Result.Failure(QuotaErrors.NotProvisioned);

        if (command.Allow)
            limit.EnablePublicSharing();
        else
            limit.DisablePublicSharing();

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
