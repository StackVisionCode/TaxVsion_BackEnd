using BuildingBlocks.Results;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Abstractions;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default);
}

public interface ISmsSender
{
    Task<Result> SendAsync(string phoneNumber, string text, CancellationToken ct = default);
}

public sealed record PushMessage(
    string Token,
    PushPlatform Platform,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data = null
);

public interface IPushSender
{
    Task<Result> SendAsync(PushMessage message, CancellationToken ct = default);
}

public interface IPushDeviceTokenRepository
{
    Task AddAsync(PushDeviceToken token, CancellationToken ct = default);

    Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default);

    Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );
}

public interface INotificationLogRepository
{
    Task AddAsync(NotificationLog log, CancellationToken ct = default);

    Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        NotificationStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    );
}
