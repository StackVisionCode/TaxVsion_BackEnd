using BuildingBlocks.Authorization;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Domain.Roles;

/// <summary>
/// Catálogo global de permisos. Los GUID son fijos y deterministas para que el
/// sembrado por migración (HasData) sea estable entre entornos.
/// </summary>
public static class PermissionCatalog
{
    // Usuarios y seguridad
    public const string UsersView = "users.view";
    public const string UsersInvite = "users.invite";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string AuditView = "audit.view";
    public const string SettingsManage = "settings.manage";
    public const string BillingView = "billing.view";
    public const string BillingManage = "billing.manage";
    public const string SubscriptionManage = "subscription.manage";

    // Módulos operativos
    public const string CustomersView = CustomersPermissions.View;
    public const string CustomersManage = CustomersPermissions.Manage;
    public const string CustomersFiscalProfileReveal = CustomersPermissions.FiscalProfileReveal;
    public const string SignaturesRequest = "signatures.request";
    public const string DocumentsView = "documents.view";
    public const string DocumentsManage = "documents.manage";
    public const string EmailUse = "email.use";
    public const string CommsCalls = "comms.calls";
    public const string CampaignsManage = "campaigns.manage";
    public const string ReportsView = "reports.view";

    // CloudStorage / Media Security Gateway
    public const string CloudStorageFileView = CloudStoragePermissions.FileView;
    public const string CloudStorageFileUpload = CloudStoragePermissions.FileUpload;
    public const string CloudStorageFileDownload = CloudStoragePermissions.FileDownload;
    public const string CloudStorageFileDelete = CloudStoragePermissions.FileDelete;
    public const string CloudStorageSettingsManage = CloudStoragePermissions.SettingsManage;
    public const string CloudStorageAuditView = CloudStoragePermissions.AuditView;

    // Signature — firma electrónica (bounded context propio, ver microservicio Signature)
    public const string SignatureRequestCreate = SignaturePermissions.RequestCreate;
    public const string SignatureRequestRead = SignaturePermissions.RequestRead;
    public const string SignatureRequestCancel = SignaturePermissions.RequestCancel;
    public const string SignatureRequestResend = SignaturePermissions.RequestResend;
    public const string SignatureRequestExpire = SignaturePermissions.RequestExpire;
    public const string SignatureDocumentPrepare = SignaturePermissions.DocumentPrepare;
    public const string SignatureDocumentSign = SignaturePermissions.DocumentSign;
    public const string SignatureDocumentView = SignaturePermissions.DocumentView;
    public const string SignatureDocumentDownload = SignaturePermissions.DocumentDownload;
    public const string SignatureDocumentAuditRead = SignaturePermissions.DocumentAuditRead;
    public const string SignatureTemplateCreate = SignaturePermissions.TemplateCreate;
    public const string SignatureTemplateUpdate = SignaturePermissions.TemplateUpdate;
    public const string SignatureTemplateDelete = SignaturePermissions.TemplateDelete;
    public const string SignatureSettingsManage = SignaturePermissions.SettingsManage;
    public const string SignaturePreparerManage = SignaturePermissions.PreparerManage;
    public const string SignatureCertificateVerify = SignaturePermissions.CertificateVerify;

    // Portal del cliente final
    public const string PortalCallsUse = "portal.calls.use";
    public const string PortalMilesUse = "portal.miles.use";
    public const string PortalFoldersView = "portal.folders.view";
    public const string PortalSignaturesSign = "portal.signatures.sign";

    public sealed record PermissionDefinition(
        Guid Id,
        string Code,
        string Module,
        string Description,
        bool IsCustomerPortal
    );

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(new Guid("a1000000-0000-0000-0000-000000000001"), UsersView, "users", "Ver usuarios del tenant", false),
        new(new Guid("a1000000-0000-0000-0000-000000000002"), UsersInvite, "users", "Invitar usuarios", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000003"),
            UsersManage,
            "users",
            "Activar, desactivar y editar usuarios",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000004"),
            RolesManage,
            "users",
            "Gestionar roles y permisos",
            false
        ),
        new(new Guid("a1000000-0000-0000-0000-000000000005"), AuditView, "audit", "Consultar auditoría", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000006"),
            SettingsManage,
            "settings",
            "Gestionar configuración del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000007"),
            BillingView,
            "billing",
            "Ver facturación y suscripción",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000008"),
            BillingManage,
            "billing",
            "Gestionar métodos de pago y facturación",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000009"),
            SubscriptionManage,
            "billing",
            "Cambiar plan y gestionar suscripción",
            false
        ),
        new(new Guid("a1000000-0000-0000-0000-000000000010"), CustomersView, "customers", "Ver clientes", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000011"),
            CustomersManage,
            "customers",
            "Crear y editar clientes",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000012"),
            SignaturesRequest,
            "signatures",
            "Solicitar firmas",
            false
        ),
        new(new Guid("a1000000-0000-0000-0000-000000000013"), DocumentsView, "documents", "Ver documentos", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000014"),
            DocumentsManage,
            "documents",
            "Gestionar documentos",
            false
        ),
        new(new Guid("a1000000-0000-0000-0000-000000000015"), EmailUse, "email", "Usar el módulo de correo", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000016"),
            CommsCalls,
            "comms",
            "Realizar llamadas y meetings",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000017"),
            CampaignsManage,
            "campaigns",
            "Gestionar campañas",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000018"),
            ReportsView,
            "reports",
            "Ver dashboard y reportes",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000019"),
            PortalCallsUse,
            "portal",
            "El cliente puede realizar llamadas",
            true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000020"),
            PortalMilesUse,
            "portal",
            "El cliente puede usar el módulo de millas",
            true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000021"),
            PortalFoldersView,
            "portal",
            "El cliente puede ver folders de su perfil",
            true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000022"),
            PortalSignaturesSign,
            "portal",
            "El cliente puede firmar documentos",
            true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000023"),
            CloudStorageFileView,
            "cloudstorage",
            "Ver metadatos de archivos",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000024"),
            CloudStorageFileUpload,
            "cloudstorage",
            "Subir archivos mediante el gateway seguro",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000025"),
            CloudStorageFileDownload,
            "cloudstorage",
            "Descargar archivos disponibles",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000026"),
            CloudStorageFileDelete,
            "cloudstorage",
            "Eliminar archivos",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000027"),
            CloudStorageSettingsManage,
            "cloudstorage",
            "Gestionar políticas de almacenamiento",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000028"),
            CloudStorageAuditView,
            "cloudstorage",
            "Consultar auditoría de archivos",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000029"),
            SignatureRequestCreate,
            "signature",
            "Crear solicitudes de firma electrónica",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000030"),
            SignatureRequestRead,
            "signature",
            "Consultar solicitudes de firma",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000031"),
            SignatureRequestCancel,
            "signature",
            "Cancelar solicitudes de firma",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000032"),
            SignatureRequestResend,
            "signature",
            "Reenviar invitaciones a firmantes",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000033"),
            SignatureRequestExpire,
            "signature",
            "Extender el vencimiento de solicitudes",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000034"),
            SignatureDocumentPrepare,
            "signature",
            "Validar y preparar documentos para firma",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000035"),
            SignatureDocumentSign,
            "signature",
            "Aplicar firma del preparador al documento",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000036"),
            SignatureDocumentView,
            "signature",
            "Ver documentos firmados y sus metadatos",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000037"),
            SignatureDocumentDownload,
            "signature",
            "Descargar sellado, original o certificado",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000038"),
            SignatureDocumentAuditRead,
            "signature",
            "Consultar el audit trail de una firma",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000039"),
            SignatureTemplateCreate,
            "signature",
            "Crear plantillas de firma reutilizables",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000040"),
            SignatureTemplateUpdate,
            "signature",
            "Modificar plantillas de firma",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000041"),
            SignatureTemplateDelete,
            "signature",
            "Eliminar plantillas de firma",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000042"),
            SignatureSettingsManage,
            "signature",
            "Gestionar la configuración de firma del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000043"),
            SignaturePreparerManage,
            "signature",
            "Gestionar firmas persistentes del preparador",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000044"),
            SignatureCertificateVerify,
            "signature",
            "Verificar certificados de firma (endpoint público)",
            false
        ),
        new(
            // OJO: "...045" a "...062" ya estan ocupados en la BD real por los 18
            // permisos de Communication (communication.chat.start etc.) sembrados
            // via migracion propia SIN pasar por este catalogo — PermissionCatalog
            // esta desincronizado de la BD para ese rango. Confirmado por consulta
            // directa a Permissions antes de elegir este GUID. Pendiente: reconciliar
            // agregando las entradas de Communication a este catalogo (fuera de
            // alcance de este cambio).
            new Guid("a1000000-0000-0000-0000-000000000063"),
            CustomersFiscalProfileReveal,
            "customers",
            "Revelar el SSN/ITIN/EIN completo de un customer",
            false
        ),
    ];

    private static readonly Dictionary<string, Guid> IdsByCode = All.ToDictionary(
        definition => definition.Code,
        definition => definition.Id
    );

    public static Guid IdOf(string code) => IdsByCode[code];

    /// <summary>Permisos por defecto de cada rol de sistema.</summary>
    public static IReadOnlyCollection<string> SystemRoleDefaults(string systemRoleName) =>
        systemRoleName switch
        {
            Role.SystemTenantAdmin => All.Where(definition => !definition.IsCustomerPortal)
                .Select(definition => definition.Code)
                .ToArray(),
            Role.SystemEmployee =>
            [
                CustomersView,
                CustomersManage,
                SignaturesRequest,
                DocumentsView,
                DocumentsManage,
                EmailUse,
                CommsCalls,
                ReportsView,
                CloudStorageFileView,
                CloudStorageFileUpload,
                CloudStorageFileDownload,
                // Signature: el empleado prepara solicitudes y consulta resultados.
                // No incluye cancel/expire/settings (reservados a TenantAdmin).
                SignatureRequestCreate,
                SignatureRequestRead,
                SignatureRequestResend,
                SignatureDocumentPrepare,
                SignatureDocumentSign,
                SignatureDocumentView,
                SignatureDocumentDownload,
            ],
            Role.SystemCustomerPortal =>
            [
                PortalFoldersView,
                PortalSignaturesSign,
                CloudStorageFileView,
                CloudStorageFileUpload,
                CloudStorageFileDownload,
            ],
            _ => [],
        };

    /// <summary>
    /// Permisos efectivos de respaldo cuando un usuario aún no tiene roles asignados
    /// (usuarios creados antes del modelo RBAC).
    /// </summary>
    public static IReadOnlyCollection<string> DefaultsFor(UserActorType actorType) =>
        actorType switch
        {
            UserActorType.TenantAdmin or UserActorType.PlatformAdmin => SystemRoleDefaults(Role.SystemTenantAdmin),
            UserActorType.TenantEmployee => SystemRoleDefaults(Role.SystemEmployee),
            UserActorType.CustomerPortal => SystemRoleDefaults(Role.SystemCustomerPortal),
            _ => [],
        };
}
