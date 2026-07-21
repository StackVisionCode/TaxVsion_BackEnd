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

/// <summary>
/// Códigos de error bien conocidos que un <see cref="IPushSender"/> puede devolver, para que
/// el caller (NotificationDispatcher, consumers con flujo manual) pueda reaccionar sin
/// acoplarse al proveedor concreto (FCM/APNs). Fase 7 del plan de notificaciones dinámicas.
/// </summary>
public static class PushErrorCodes
{
    /// <summary>
    /// El token ya no es válido en el proveedor (app desinstalada, token expirado). El caller
    /// debe revocar el <see cref="PushDeviceToken"/> correspondiente para no seguir
    /// reintentando indefinidamente contra un dispositivo fantasma.
    /// </summary>
    public const string TokenInvalid = "Notification.Push.TokenInvalid";
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

    /// <summary>
    /// Revoca el token si todavía existe y está activo — usado cuando el proveedor push
    /// (<see cref="PushErrorCodes.TokenInvalid"/>) confirma que ya no es entregable. No
    /// persiste por sí sola — el caller ya tiene su propio <c>IUnitOfWork.SaveChangesAsync</c>
    /// en el mismo flujo (mismo criterio que <see cref="AddAsync"/>).
    /// </summary>
    Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default);
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
