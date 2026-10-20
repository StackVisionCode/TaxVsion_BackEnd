using System.Collections.Frozen;

namespace TaxVision.Notification.Domain.Catalog;

/// <summary>
/// Strings estables del contrato <c>notification.inapp.requested.v1</c> (PARTE 3.4).
/// El nombre del contrato no puede derivarse del enum "a ojo" (EmailThread → email_thread),
/// así que se mapea explícitamente para que un rename del enum no cambie el contrato en silencio.
/// </summary>
public static class NotificationContractNames
{
    private static readonly FrozenDictionary<NotificationResourceType, string> ResourceTypes = new Dictionary<
        NotificationResourceType,
        string
    >
    {
        [NotificationResourceType.Task] = "task",
        [NotificationResourceType.Reminder] = "reminder",
        [NotificationResourceType.ClientRequest] = "client_request",
        [NotificationResourceType.EmailThread] = "email_thread",
        [NotificationResourceType.SignatureRequest] = "signature_request",
        [NotificationResourceType.Meeting] = "meeting",
        [NotificationResourceType.Appointment] = "appointment",
        [NotificationResourceType.Conversation] = "conversation",
        [NotificationResourceType.Recording] = "recording",
        [NotificationResourceType.CustomerDocuments] = "customer_documents",
        [NotificationResourceType.File] = "file",
        [NotificationResourceType.StorageSettings] = "storage_settings",
        [NotificationResourceType.Billing] = "billing",
        [NotificationResourceType.Invoice] = "invoice",
        [NotificationResourceType.MailboxSettings] = "mailbox_settings",
        [NotificationResourceType.SecuritySettings] = "security_settings",
        [NotificationResourceType.ImportJob] = "import_job",
        [NotificationResourceType.SupportTicket] = "support_ticket",
        [NotificationResourceType.Seats] = "seats",
        [NotificationResourceType.Document] = "document",
    }.ToFrozenDictionary();

    public static string For(NotificationResourceType resourceType) => ResourceTypes[resourceType];

    public static string For(NotificationAction action) =>
        action switch
        {
            NotificationAction.Open => "open",
            NotificationAction.Review => "review",
            NotificationAction.Sign => "sign",
            NotificationAction.Pay => "pay",
            NotificationAction.Reconnect => "reconnect",
            _ => "open",
        };

    public static string For(NotificationCatalogCategory category) => category.ToString();

    public static string For(NotificationPriority priority) => priority.ToString();
}
