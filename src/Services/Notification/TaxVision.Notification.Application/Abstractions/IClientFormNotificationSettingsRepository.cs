namespace TaxVision.Notification.Application.Abstractions;

public interface IClientFormNotificationSettingsRepository
{
    Task<bool> IsPushEnabledAsync(Guid tenantId, CancellationToken ct = default);

    Task<Domain.Directory.ClientFormNotificationSettingsProjection?> GetAsync(
        Guid tenantId,
        CancellationToken ct = default
    );

    Task AddAsync(Domain.Directory.ClientFormNotificationSettingsProjection row, CancellationToken ct = default);
}
