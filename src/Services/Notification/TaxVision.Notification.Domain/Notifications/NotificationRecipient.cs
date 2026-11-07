using BuildingBlocks.Results;

namespace TaxVision.Notification.Domain.Notifications;

/// <summary>
/// A quién va una notificación. <see cref="NotificationLog.Recipient"/> sigue siendo un string: cada fábrica produce el
/// formato de siempre, y la deduplicación por destinatario depende de él.
/// </summary>
public sealed record NotificationRecipient
{
    public const int MaxLength = 320;
    private const string UnknownGuest = "unknown";

    private static readonly Error Required = new("Notification.Recipient", "Recipient is required.");

    public string Value { get; }

    private NotificationRecipient(string value) => Value = value;

    /// <summary>Misma regla que Postmaster: si aquí pasa, allá no lo rechaza.</summary>
    public static bool IsValidEmail(string? address)
    {
        var trimmed = address?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && trimmed.Length <= MaxLength
            && trimmed.Contains('@')
            && !trimmed.StartsWith('@')
            && !trimmed.EndsWith('@');
    }

    public static Result<NotificationRecipient> ForEmail(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return Result.Failure<NotificationRecipient>(Required);

        return IsValidEmail(address)
            ? Result.Success(new NotificationRecipient(address.Trim()))
            : Result.Failure<NotificationRecipient>(
                new Error("Notification.Recipient", "Recipient is not a valid email address.")
            );
    }

    /// <summary>Tal como llegó: el formato del número lo valida el servicio de Sms.</summary>
    public static Result<NotificationRecipient> ForPhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone)
            ? Result.Failure<NotificationRecipient>(Required)
            : Result.Success(new NotificationRecipient(phone.Trim()));

    public static NotificationRecipient ForUser(Guid userId) => new($"user:{userId:N}");

    public static NotificationRecipient ForDevice(Guid userId, Guid deviceId) => new($"{userId:N}:{deviceId:N}");

    /// <summary>Push sin dirección: el destino son los dispositivos de ese usuario o firmante.</summary>
    public static NotificationRecipient ForPushTarget(Guid targetId) => new(targetId.ToString());

    /// <summary>Invitado sin cuenta: una etiqueta para el historial, no una dirección de entrega.</summary>
    public static NotificationRecipient ForGuest(string? label) =>
        new(string.IsNullOrWhiteSpace(label) ? UnknownGuest : label.Trim());
}
