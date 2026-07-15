using BuildingBlocks.Authorization;
using TaxVision.Auth.Domain.Tenants;
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
    public const string TenantDomainsManage = "tenant.domains.manage";

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
    public const string CloudStorageRecycleBinManage = CloudStoragePermissions.RecycleBinManage;
    public const string CloudStorageFolderManage = CloudStoragePermissions.FolderManage;
    public const string CloudStorageShareCreate = CloudStoragePermissions.ShareCreate;
    public const string CloudStorageShareRevoke = CloudStoragePermissions.ShareRevoke;
    public const string CloudStorageShareManage = CloudStoragePermissions.ShareManage;
    public const string CloudStorageLegalManage = CloudStoragePermissions.LegalManage;
    public const string CloudStorageDmcaCounterNotice = CloudStoragePermissions.DmcaCounterNotice;

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

    // Communication — chat, llamadas, meetings (bounded context propio, ver microservicio
    // Communication). Los 18 GUID/Code de abajo YA existen como filas reales en la tabla
    // Permissions (sembradas por SQL directo en la migración AddCommunicationPermissions,
    // 2026-07-10) — nunca habían pasado por este catálogo (desfase documentado desde
    // entonces). Se reconcilian aquí con los MISMOS GUID exactos; la migración que agrega
    // MinPlanTier/IsAssignableByTenant debe usar UpdateData (no InsertData) para estas 18 filas.
    public const string CommunicationChatStart = CommunicationPermissions.ChatStart;
    public const string CommunicationChatReply = CommunicationPermissions.ChatReply;
    public const string CommunicationChatModerate = CommunicationPermissions.ChatModerate;
    public const string CommunicationSupportOpen = CommunicationPermissions.SupportOpen;
    public const string CommunicationSupportAgent = CommunicationPermissions.SupportAgent;
    public const string CommunicationCallStart = CommunicationPermissions.CallStart;
    public const string CommunicationVideoCallStart = CommunicationPermissions.VideoCallStart;
    public const string CommunicationCallRecord = CommunicationPermissions.CallRecord;
    public const string CommunicationMeetingCreate = CommunicationPermissions.MeetingCreate;
    public const string CommunicationMeetingJoin = CommunicationPermissions.MeetingJoin;
    public const string CommunicationMeetingHost = CommunicationPermissions.MeetingHost;
    public const string CommunicationMeetingRecord = CommunicationPermissions.MeetingRecord;
    public const string CommunicationScreenshotCreate = CommunicationPermissions.ScreenshotCreate;
    public const string CommunicationGroupCreate = CommunicationPermissions.GroupCreate;
    public const string CommunicationGroupManageMembers = CommunicationPermissions.GroupManageMembers;
    public const string CommunicationNotificationRead = CommunicationPermissions.NotificationRead;
    public const string CommunicationSettingsManage = CommunicationPermissions.SettingsManage;
    public const string CommunicationAnalyticsRead = CommunicationPermissions.AnalyticsRead;

    public sealed record PermissionDefinition(
        Guid Id,
        string Code,
        string Module,
        string Description,
        bool IsCustomerPortal,
        int MinPlanTier = (int)PlanTier.Starter,
        bool IsAssignableByTenant = true
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
            // Reservado: quien controla roles.manage puede asignar CUALQUIER rol (incluido
            // Tenant Admin) a cualquier usuario — es el vector de escalada de privilegios más
            // directo. Nunca asignable a un rol custom, solo lo tienen los roles de sistema.
            new Guid("a1000000-0000-0000-0000-000000000004"),
            RolesManage,
            "users",
            "Gestionar roles y permisos",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            IsAssignableByTenant: false
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
            // Reservado: facturación/billing es responsabilidad exclusiva del Tenant Admin —
            // ver Subscription (fuera de alcance de este cambio, solo se marca el guardarraíl).
            new Guid("a1000000-0000-0000-0000-000000000007"),
            BillingView,
            "billing",
            "Ver facturación y suscripción",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            IsAssignableByTenant: false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000008"),
            BillingManage,
            "billing",
            "Gestionar métodos de pago y facturación",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            IsAssignableByTenant: false
        ),
        new(
            // Reservado: incluye compra/baja de asientos — impacta directamente la facturación.
            new Guid("a1000000-0000-0000-0000-000000000009"),
            SubscriptionManage,
            "billing",
            "Cambiar plan y gestionar suscripción",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            IsAssignableByTenant: false
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
        new(
            // Módulo "email" solo disponible desde el plan Pro (ver SubscriptionPlanCatalogSeeder).
            new Guid("a1000000-0000-0000-0000-000000000015"),
            EmailUse,
            "email",
            "Usar el módulo de correo",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            // Módulo "comms" solo disponible desde el plan Pro.
            new Guid("a1000000-0000-0000-0000-000000000016"),
            CommsCalls,
            "comms",
            "Realizar llamadas y meetings",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            // Módulo "campaigns" solo disponible desde el plan Pro.
            new Guid("a1000000-0000-0000-0000-000000000017"),
            CampaignsManage,
            "campaigns",
            "Gestionar campañas",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            // Módulo "reports" solo disponible desde el plan Pro.
            new Guid("a1000000-0000-0000-0000-000000000018"),
            ReportsView,
            "reports",
            "Ver dashboard y reportes",
            false,
            MinPlanTier: (int)PlanTier.Pro
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
            new Guid("a1000000-0000-0000-0000-000000000065"),
            CloudStorageRecycleBinManage,
            "cloudstorage",
            "Restaurar y purgar archivos de la papelera",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000066"),
            CloudStorageFolderManage,
            "cloudstorage",
            "Crear, renombrar y mover carpetas de archivos",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000067"),
            CloudStorageShareCreate,
            "cloudstorage",
            "Crear links para compartir archivos",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000068"),
            CloudStorageShareRevoke,
            "cloudstorage",
            "Revocar links de compartir existentes",
            false
        ),
        new(
            // Reservado: habilita otorgar permisos de Upload/EditMetadata en un
            // link publico y cambiar la expiracion de cualquier link del tenant —
            // ambos con impacto directo en la exposicion de datos fiscales.
            new Guid("a1000000-0000-0000-0000-000000000069"),
            CloudStorageShareManage,
            "cloudstorage",
            "Otorgar permisos elevados en links y gestionar su expiracion",
            false,
            IsAssignableByTenant: false
        ),
        new(
            // Reservado: legal hold + DMCA (takedown/reinstate) es
            // exclusivo del equipo legal de la plataforma, nunca de un tenant.
            new Guid("a1000000-0000-0000-0000-000000000070"),
            CloudStorageLegalManage,
            "cloudstorage",
            "Gestionar legal hold y takedowns DMCA",
            false,
            IsAssignableByTenant: false
        ),
        new(
            // A diferencia de LegalManage, esto lo ejerce el propio tenant sobre
            // sus archivos (responder a un takedown recibido) — mismo nivel de
            // TenantAdmin-only que CloudStorageFileDelete, no de plataforma.
            new Guid("a1000000-0000-0000-0000-000000000071"),
            CloudStorageDmcaCounterNotice,
            "cloudstorage",
            "Presentar contranotificacion DMCA sobre un archivo propio",
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
            new Guid("a1000000-0000-0000-0000-000000000063"),
            CustomersFiscalProfileReveal,
            "customers",
            "Revelar el SSN/ITIN/EIN completo de un customer",
            false
        ),
        new(
            // Reservado: quien agrega/deshabilita dominios controla qué Host puede
            // autenticar como este tenant (Fase A5) — riesgo equivalente a
            // RolesManage/BillingManage. Nunca asignable a un rol custom.
            new Guid("a1000000-0000-0000-0000-000000000064"),
            TenantDomainsManage,
            "domains",
            "Gestionar dominios propios del tenant (custom hostnames)",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            IsAssignableByTenant: false
        ),
        // --- Communication (reconciliado, ver comentario arriba) ---
        new(
            new Guid("a1000000-0000-0000-0000-000000000045"),
            CommunicationChatStart,
            "communication",
            "Iniciar conversaciones de chat",
            true,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000046"),
            CommunicationChatReply,
            "communication",
            "Responder en conversaciones de chat",
            true,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000047"),
            CommunicationChatModerate,
            "communication",
            "Moderar mensajes en conversaciones del tenant",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000048"),
            CommunicationSupportOpen,
            "communication",
            "Abrir chat de soporte hacia el PlatformTenant",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000049"),
            CommunicationSupportAgent,
            "communication",
            "Atender chats de soporte como agente (PlatformTenant)",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000050"),
            CommunicationCallStart,
            "communication",
            "Iniciar llamadas de audio 1:1",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000051"),
            CommunicationVideoCallStart,
            "communication",
            "Iniciar llamadas de video 1:1",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000052"),
            CommunicationCallRecord,
            "communication",
            "Grabar llamadas 1:1 (con banner de disclosure)",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000053"),
            CommunicationMeetingCreate,
            "communication",
            "Crear reuniones multi-party",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000054"),
            CommunicationMeetingJoin,
            "communication",
            "Unirse a reuniones (previa invitación válida)",
            true,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000055"),
            CommunicationMeetingHost,
            "communication",
            "Actuar como host de reuniones (waiting room, mute all, transfer)",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000056"),
            CommunicationMeetingRecord,
            "communication",
            "Grabar reuniones (con banner de disclosure)",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000057"),
            CommunicationScreenshotCreate,
            "communication",
            "Adjuntar screenshots/voice/video en chat",
            true,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000058"),
            CommunicationGroupCreate,
            "communication",
            "Crear grupos internos por tenant",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000059"),
            CommunicationGroupManageMembers,
            "communication",
            "Gestionar miembros de grupos internos",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000060"),
            CommunicationNotificationRead,
            "communication",
            "Consultar notificaciones in-app propias",
            true,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000061"),
            CommunicationSettingsManage,
            "communication",
            "Gestionar la configuración de Communication del tenant",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000062"),
            CommunicationAnalyticsRead,
            "communication",
            "Consultar analytics de Communication del tenant",
            false,
            MinPlanTier: (int)PlanTier.Pro
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
                // Organizar archivos en carpetas es trabajo operativo diario, no
                // administrativo — a diferencia de recyclebin.manage/settings/audit.
                CloudStorageFolderManage,
                // Compartir/revocar un archivo puntual es trabajo operativo; otorgar
                // Upload/EditMetadata en un link o tocar su expiracion queda en
                // share.manage, reservado a TenantAdmin (ver PermissionDefinition).
                CloudStorageShareCreate,
                CloudStorageShareRevoke,
                // Signature: el empleado prepara solicitudes y consulta resultados.
                // No incluye cancel/expire/settings (reservados a TenantAdmin).
                SignatureRequestCreate,
                SignatureRequestRead,
                SignatureRequestResend,
                SignatureDocumentPrepare,
                SignatureDocumentSign,
                SignatureDocumentView,
                SignatureDocumentDownload,
                // Communication: mismo set que sembró la migración AddCommunicationPermissions
                // para el rol "Employee" — nunca host de settings/analytics/moderate/record.
                CommunicationChatStart,
                CommunicationChatReply,
                CommunicationSupportOpen,
                CommunicationCallStart,
                CommunicationVideoCallStart,
                CommunicationMeetingCreate,
                CommunicationMeetingJoin,
                CommunicationMeetingHost,
                CommunicationScreenshotCreate,
                CommunicationNotificationRead,
            ],
            Role.SystemCustomerPortal =>
            [
                PortalFoldersView,
                PortalSignaturesSign,
                CloudStorageFileView,
                CloudStorageFileUpload,
                CloudStorageFileDownload,
                // Communication: mismo set que sembró la migración AddCommunicationPermissions
                // para el rol "Customer Portal" — nunca moderate/host/record/settings.
                CommunicationChatStart,
                CommunicationChatReply,
                CommunicationSupportOpen,
                CommunicationMeetingJoin,
                CommunicationScreenshotCreate,
                CommunicationNotificationRead,
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
