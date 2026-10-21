namespace TaxVision.Tasks.ClientForms.Domain.Audit;

public static class FormAuditAction
{
    public const string TemplateCreated = "template.created";
    public const string TemplatePublished = "template.published";
    public const string TemplateArchived = "template.archived";
    public const string TemplateRestored = "template.restored";
    public const string TemplateDuplicated = "template.duplicated";
    public const string TemplateDeleted = "template.deleted";
    public const string ModuleAvailabilityChanged = "module.availability_changed";
}
