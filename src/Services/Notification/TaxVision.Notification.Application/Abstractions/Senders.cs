using BuildingBlocks.Results;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Abstractions;

public interface ISmsSender
{
    // El idempotencyToken debe ser estable e independiente del texto (ej. EventId
    // del hecho que originó el SMS). Sin él, la clave se deriva del cuerpo y los
    // SMS de OTP quedan reversibles por fuerza bruta desde el log.
    Task<Result> SendAsync(
        string phoneNumber,
        string text,
        CancellationToken ct,
        string? templateKey,
        string? idempotencyToken,
        string? sourceRef = null
    );
}

// Atajos sobre el único miembro del puerto: fuera de la interfaz para que ningún adaptador los herede.
public static class SmsSenderExtensions
{
    public static Task<Result> SendAsync(
        this ISmsSender sender,
        string phoneNumber,
        string text,
        CancellationToken ct = default
    ) => sender.SendAsync(phoneNumber, text, ct, templateKey: null, idempotencyToken: null);

    public static Task<Result> SendAsync(
        this ISmsSender sender,
        string phoneNumber,
        string text,
        CancellationToken ct,
        string? templateKey
    ) => sender.SendAsync(phoneNumber, text, ct, templateKey, idempotencyToken: null);
}

public sealed record PushMessage(
    string Token,
    PushPlatform Platform,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data = null,
    string? Topic = null,
    string? Tag = null,
    string? Link = null,
    // Vida del aviso en FCM (Android y web). Null = lo de siempre.
    TimeSpan? TimeToLive = null
);

/// <summary>
/// Rastro del push fuera de la transacción del consumer (NOT-07): el log se confirma antes de llamar a FCM y el
/// resultado en otra unidad. Un rollback posterior no lo borra, y el reintento encuentra lo ya enviado.
/// </summary>
public interface IPushAttemptRecorder
{
    Task RecordAttemptAsync(NotificationLog log, CancellationToken ct = default);

    Task RecordSentAsync(Guid tenantId, Guid logId, CancellationToken ct = default);

    Task RecordFailedAsync(Guid tenantId, Guid logId, string error, CancellationToken ct = default);
}

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
    public const string InvalidArgument = "Notification.Push.InvalidArgument";
    public const string SenderIdMismatch = "Notification.Push.SenderIdMismatch";
    public const string ThirdPartyAuthError = "Notification.Push.ThirdPartyAuthError";
    public const string Unavailable = "Notification.Push.Unavailable";
    public const string Internal = "Notification.Push.Internal";
    public const string QuotaExceeded = "Notification.Push.QuotaExceeded";

    /// <summary>No hay proveedor push configurado; no es transitorio: se arregla con la credencial.</summary>
    public const string NotConfigured = "Notification.Push.NotConfigured";

    /// <summary>Errores que se reintentan (P-21.4); el resto no mejora con otro intento.</summary>
    public static bool IsTransient(string code) => code is Unavailable or Internal or QuotaExceeded;
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

    /// <summary>
    /// Revoca todos los tokens activos del usuario (suspensión o retiro) y devuelve cuántos. No
    /// persiste: el caller guarda.
    /// </summary>
    Task<int> RevokeAllActiveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Revoca los activos del usuario registrados con esa sesión. No persiste.</summary>
    Task<int> RevokeForSessionAsync(Guid tenantId, Guid userId, Guid sessionId, CancellationToken ct = default);

    /// <summary>Revoca los activos del usuario salvo los de <paramref name="exceptSessionId"/>. No persiste.</summary>
    Task<int> RevokeAllActiveForUserExceptSessionAsync(
        Guid tenantId,
        Guid userId,
        Guid? exceptSessionId,
        CancellationToken ct = default
    );

    /// <summary>
    /// La última alta gana: desactiva el mismo token en otras oficinas. Solo desactiva y exige el token
    /// exacto. No persiste.
    /// </summary>
    Task<int> DeactivateTokenInOtherTenantsAsync(string token, Guid exceptTenantId, CancellationToken ct = default);
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

    /// <summary>Chequeo de idempotencia para <c>IEmailDispatchGateway.QueueEmailAsync</c> — busca un
    /// log ya creado para el mismo evento de origen, plantilla Y destinatario, para que un reintento de
    /// Wolverine (misma entrega, el handler se re-ejecuta desde cero) no cree un segundo
    /// <see cref="NotificationLog"/> ni dispare un segundo envío real. El destinatario ES parte de la
    /// clave: un evento que hace fan-out a varios firmantes (mismo evento+plantilla, distinto correo) NO
    /// es un reintento y no debe deduparse. Ver comentario en <c>EventBasedEmailDispatchGateway.QueueEmailAsync</c>.</summary>
    Task<NotificationLog?> GetByRelatedEventIdAsync(
        Guid tenantId,
        Guid relatedEventId,
        string templateKey,
        string recipient,
        CancellationToken ct = default
    );

    Task<NotificationLog?> GetByRelatedEventAsync(
        Guid tenantId,
        Guid relatedEventId,
        string kind,
        NotificationChannel channel,
        string recipient,
        CancellationToken ct = default
    );
}
