namespace BuildingBlocks.Authorization;

/// <summary>Scopes OAuth M2M aceptados por los endpoints internos de Documents (audience
/// taxvision-documents). Contratos entre servicios; NO se asignan a roles humanos.</summary>
public static class DocumentsServiceScopes
{
    public const string Generate = "documents.generate";
    public const string GenerateBatch = "documents.generate-batch";
    public const string ReadStatus = "documents.read-status";
    public const string Retry = "documents.retry";
    public const string Cancel = "documents.cancel";
    public const string TemplatesRead = "documents.templates.read";
    public const string TemplatesManage = "documents.templates.manage";
    public const string Preview = "documents.preview";
}
