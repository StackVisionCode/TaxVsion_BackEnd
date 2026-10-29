namespace TaxVision.Tasks.ClientForms.Domain.Audit;

public static class FormAuditAction
{
    public const string TemplateCreated = "template.created";
    public const string TemplatePublished = "template.published";
    public const string TemplateArchived = "template.archived";
    public const string TemplateRestored = "template.restored";
    public const string TemplateDuplicated = "template.duplicated";
    public const string TemplateDeleted = "template.deleted";
    public const string TemplateClonedFromLibrary = "template.cloned_from_library";
    public const string LibraryReviewed = "library.reviewed";
    public const string LibraryListed = "library.listed";
    public const string LibraryUnlisted = "library.unlisted";
    public const string ModuleAvailabilityChanged = "module.availability_changed";
    public const string IntakeLinkCreated = "intake_link.created";
    public const string IntakeLinkUpdated = "intake_link.updated";
    public const string IntakeLinkTokenViewed = "intake_link.token_viewed";
    public const string ConsentNoticeCreated = "consent_notice.created";
    public const string ConsentNoticeUpdated = "consent_notice.updated";
    public const string ConsentNoticePublished = "consent_notice.published";
    public const string ConsentNoticeArchived = "consent_notice.archived";
    public const string ConsentPresented = "consent.presented";
    public const string ConsentAccepted = "consent.accepted";
    public const string ConsentDeclined = "consent.declined";
    public const string RetentionPurged = "retention.purged";
}
