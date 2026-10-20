namespace TaxVision.Notification.Domain.Catalog;

public enum NotificationRecipientPolicy
{
    ExplicitUser,
    ByPermission,
    Participants,
    MailboxAudience,
    CustomerPortal,
    TenantOwner,
}
