namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del microservicio Signature. Mismo patrón que
/// <see cref="NotificationPermissions"/> y <see cref="CloudStoragePermissions"/>:
/// claves punteadas en minúsculas usadas como claim "perm" en el JWT y como policy en
/// los endpoints via <c>[HasPermission(SignaturePermissions.RequestCreate)]</c>.
///
/// Los admins (TenantAdmin/PlatformAdmin) pasan siempre; el resto necesita el claim
/// específico.
/// </summary>
public static class SignaturePermissions
{
    // Solicitudes de firma
    public const string RequestCreate = "signature.request.create";
    public const string RequestRead = "signature.request.read";
    public const string RequestCancel = "signature.request.cancel";
    public const string RequestResend = "signature.request.resend";
    public const string RequestExpire = "signature.request.expire";

    // Documentos y firma
    public const string DocumentPrepare = "signature.document.prepare";
    public const string DocumentSign = "signature.document.sign";
    public const string DocumentView = "signature.document.view";
    public const string DocumentDownload = "signature.document.download";
    public const string DocumentAuditRead = "signature.document.audit.read";

    // Plantillas de firma reutilizables
    public const string TemplateCreate = "signature.template.create";
    public const string TemplateUpdate = "signature.template.update";
    public const string TemplateDelete = "signature.template.delete";

    // Configuración por tenant (retención, canales de verificación, etc.)
    public const string SettingsManage = "signature.settings.manage";

    // Firma persistente del preparador (imagen aplicada por el CRM)
    public const string PreparerManage = "signature.preparer.manage";

    // Verificación pública de un certificado (link con token)
    public const string CertificateVerify = "signature.certificate.verify";

    // Restricciones de plan controladas por la PLATAFORMA (no exponer al SDK de tenant).
    // Solo el PlatformAdmin o un servicio interno obtiene este claim en su JWT.
    public const string PlanConstraintsManage = "signature.constraints.manage";
}
