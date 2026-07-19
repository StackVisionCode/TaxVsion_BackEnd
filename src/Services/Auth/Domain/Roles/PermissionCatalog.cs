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
    public const string BrandingManage = "branding.manage";

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

    // Techos de plan (signature.constraints.manage) — nunca estuvo en este catálogo pese a que
    // SignatureAdminController lo exige desde que se creó: sin fila real, el chequeo de
    // [HasPermission] dependía por completo del bypass de rol (ver HasPermission() en cada
    // ClaimsPrincipalExtensions), que se retiró para TenantAdmin. Sembrado ahora con
    // PlatformOnly: true — nunca lo tiene el rol de sistema "Tenant Admin" por defecto.
    public const string SignaturePlanConstraintsManage = SignaturePermissions.PlanConstraintsManage;

    // Correspondence — inbox filtrado por customer (bounded context propio, ver microservicio
    // Correspondence). La Fase 5 registró correspondence.read; la Fase 8 agrega
    // attachment.download (disparar la descarga bajo demanda + pedir su URL firmada); la Fase 11
    // agrega compose (crear/editar/autoguardar/descartar un Draft) y reply (arrancar/reutilizar un
    // reply sobre un mensaje entrante) — independientes entre sí (plan §27); la Fase 14 agrega
    // send (enviar un Draft ya redactado, llama a Postmaster). admin se registra en una fase
    // futura, no antes (YAGNI).
    public const string CorrespondenceRead = CorrespondencePermissions.Read;
    public const string CorrespondenceAttachmentDownload = CorrespondencePermissions.AttachmentDownload;
    public const string CorrespondenceCompose = CorrespondencePermissions.Compose;
    public const string CorrespondenceReply = CorrespondencePermissions.Reply;
    public const string CorrespondenceSend = CorrespondencePermissions.Send;

    // Connectors — cuentas de correo conectadas (OAuth Gmail/Graph o IMAP+SMTP manual) que
    // alimentan el envío/recepción de Correspondence (bounded context propio, ver microservicio
    // Connectors). Fase 6.5 (hardening): estos dos permisos ya los exigían los controllers de
    // Connectors vía [HasPermission(...)] desde que se construyeron, pero nunca se habían
    // sembrado en este catálogo — sin fila real, ningún rol podía tenerlos asignados.
    public const string ConnectorsAccountsRead = ConnectorsPermissions.AccountsRead;
    public const string ConnectorsAccountsWrite = ConnectorsPermissions.AccountsWrite;

    // Scribe — templates/layouts de correo, event mappings y render (bounded context propio, ver
    // microservicio Scribe). Fase 10.5 (hardening): estos 9 permisos ya los exigían los 4
    // controllers de Scribe vía [HasPermission(...)] desde que se construyeron, pero nunca se
    // habían sembrado en este catálogo — mismo gap exacto que Connectors (Fase 6.5). ScribeRender
    // es distinto de los otros 8: no lo usa ningún endpoint humano, lo exige únicamente
    // RenderController ("POST /scribe/render") para el caller M2M de Notification
    // (ScribeRenderClient) — se sembró como fila real para que el token de servicio pueda llevarlo
    // como claim "perm" (ver ServiceAuth:Clients en Auth), no para que un rol humano lo reciba (ver
    // el comentario junto a su PermissionDefinition más abajo).
    public const string ScribeTemplatesRead = ScribePermissions.TemplatesRead;
    public const string ScribeTemplatesWrite = ScribePermissions.TemplatesWrite;
    public const string ScribeLayoutsRead = ScribePermissions.LayoutsRead;
    public const string ScribeLayoutsWrite = ScribePermissions.LayoutsWrite;
    public const string ScribeEventMappingsRead = ScribePermissions.EventMappingsRead;
    public const string ScribeEventMappingsWrite = ScribePermissions.EventMappingsWrite;
    public const string ScribeCampaignsRead = ScribePermissions.CampaignsRead;
    public const string ScribeCampaignsWrite = ScribePermissions.CampaignsWrite;
    public const string ScribeRender = ScribePermissions.Render;

    // Postmaster — envío/entrega de correo, proveedores por tenant y suppression list (bounded
    // context propio, ver microservicio Postmaster). Encontrado durante la auditoría de
    // aislamiento por tenant_id (2026-07-18): estos 5 permisos ya los exigían los 3 controllers
    // de Postmaster vía [HasPermission(...)] desde que se construyeron, pero nunca se habían
    // sembrado en este catálogo — mismo gap exacto que Connectors (Fase 6.5) y Scribe (Fase
    // 10.5), esta vez descubierto porque el TenantAdmin de una oficina real recibió 403 en
    // ProvidersController tras retirarse el bypass de rol (sin fila real, "perm" nunca se poblaba
    // salvo por el bypass). ProvidersWrite cubre también PUT /postmaster/system/provider/{code}
    // (el proveedor default de plataforma), pero ese endpoint ya trae su propio chequeo inline
    // `User.IsInRole("PlatformAdmin")` — no hace falta PlatformOnly aquí.
    public const string PostmasterMessagesRead = PostmasterPermissions.MessagesRead;
    public const string PostmasterSuppressionRead = PostmasterPermissions.SuppressionRead;
    public const string PostmasterSuppressionWrite = PostmasterPermissions.SuppressionWrite;
    public const string PostmasterProvidersRead = PostmasterPermissions.ProvidersRead;
    public const string PostmasterProvidersWrite = PostmasterPermissions.ProvidersWrite;

    // Notification — configuración SMTP/API, envío/historial, templates/layouts, campañas y logs
    // (bounded context propio, ver microservicio Notification). Mismo gap y mismo hallazgo que
    // Postmaster arriba: 8 de estos 9 permisos ya los exigían los 5 controllers de Notification
    // vía [HasPermission(...)], pero nunca se habían sembrado en este catálogo. LogView no lo usa
    // ningún controller todavía (reservado, mismo criterio que ScribeCampaignsRead/Write) — se
    // siembra igual porque el código ya define la constante.
    public const string NotificationSettingsManage = NotificationPermissions.SettingsManage;
    public const string NotificationEmailSend = NotificationPermissions.EmailSend;
    public const string NotificationEmailView = NotificationPermissions.EmailView;
    public const string NotificationTemplateView = NotificationPermissions.TemplateView;
    public const string NotificationTemplateManage = NotificationPermissions.TemplateManage;
    public const string NotificationLayoutManage = NotificationPermissions.LayoutManage;
    public const string NotificationCampaignView = NotificationPermissions.CampaignView;
    public const string NotificationCampaignManage = NotificationPermissions.CampaignManage;
    public const string NotificationLogView = NotificationPermissions.LogView;

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

    // PaymentApp / PaymentClient — pagos SaaS de plataforma y pagos que un tenant cobra a sus
    // propios clientes (bounded contexts propios, ver microservicios PaymentApp/PaymentClient).
    // Sembrados junto con el merge que trajo ambos servicios (2026-07-18): sus
    // ClaimsPrincipalExtensions.HasPermission ya traían el mismo bypass de rol
    // ("TenantAdmin" pasa siempre) que se retiró de los otros 9 servicios en esta misma
    // auditoría — se corrige acá antes de que llegue a producción, no después. AdminCrossTenant
    // (ambos) es PlatformOnly: true — su propio controller (PaymentAppAdminController /
    // PaymentClientAdminController) documenta que el tenant es un filtro OPCIONAL, no una
    // restricción, así que sin PlatformOnly cualquier TenantAdmin vería pagos de cualquier
    // otro tenant por defecto.
    public const string PaymentAppSaaSPaymentRead = PaymentAppPermissions.SaaSPaymentRead;
    public const string PaymentAppSaaSPaymentRefund = PaymentAppPermissions.SaaSPaymentRefund;
    public const string PaymentAppProviderCustomerRead = PaymentAppPermissions.ProviderCustomerRead;
    public const string PaymentAppProviderCustomerManage = PaymentAppPermissions.ProviderCustomerManage;
    public const string PaymentAppAdminCrossTenant = PaymentAppPermissions.AdminCrossTenant;

    public const string PaymentClientConfigRead = PaymentClientPermissions.ConfigRead;
    public const string PaymentClientConfigManage = PaymentClientPermissions.ConfigManage;
    public const string PaymentClientPaymentRead = PaymentClientPermissions.PaymentRead;
    public const string PaymentClientPaymentCharge = PaymentClientPermissions.PaymentCharge;
    public const string PaymentClientPaymentRefund = PaymentClientPermissions.PaymentRefund;
    public const string PaymentClientPaymentLinkRead = PaymentClientPermissions.PaymentLinkRead;
    public const string PaymentClientPaymentLinkManage = PaymentClientPermissions.PaymentLinkManage;
    public const string PaymentClientConnectAccountRead = PaymentClientPermissions.ConnectAccountRead;
    public const string PaymentClientConnectAccountOnboard = PaymentClientPermissions.ConnectAccountOnboard;
    public const string PaymentClientPayoutRead = PaymentClientPermissions.PayoutRead;
    public const string PaymentClientPayoutManage = PaymentClientPermissions.PayoutManage;
    public const string PaymentClientRecurringRead = PaymentClientPermissions.RecurringRead;
    public const string PaymentClientRecurringManage = PaymentClientPermissions.RecurringManage;
    public const string PaymentClientAdminCrossTenant = PaymentClientPermissions.AdminCrossTenant;

    public sealed record PermissionDefinition(
        Guid Id,
        string Code,
        string Module,
        string Description,
        bool IsCustomerPortal,
        int MinPlanTier = (int)PlanTier.Starter,
        bool IsAssignableByTenant = true,
        bool PlatformOnly = false
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
            new Guid("a1000000-0000-0000-0000-000000000072"),
            CorrespondenceRead,
            "correspondence",
            "Ver la bandeja de correspondencia con customers",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000073"),
            CorrespondenceAttachmentDownload,
            "correspondence",
            "Descargar adjuntos de la bandeja de correspondencia",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000074"),
            CorrespondenceCompose,
            "correspondence",
            "Crear, editar y descartar borradores de correspondencia",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000075"),
            CorrespondenceReply,
            "correspondence",
            "Responder a un mensaje entrante de correspondencia",
            false
        ),
        new(
            // Enviar es una acción irreversible (llama a Postmaster, un correo real sale por el
            // proveedor conectado) — riesgo distinto de Compose/Reply, mismo criterio que separó
            // esos dos entre sí (plan §27, Fase 14).
            new Guid("a1000000-0000-0000-0000-000000000076"),
            CorrespondenceSend,
            "correspondence",
            "Enviar un borrador de correspondencia ya redactado",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000077"),
            ConnectorsAccountsRead,
            "connectors",
            "Ver las cuentas de correo conectadas del tenant",
            false
        ),
        new(
            // A diferencia de ConnectorsAccountsRead, conectar/reconectar/desconectar una cuenta
            // implica un intercambio OAuth real o credenciales IMAP/SMTP en texto plano — mismo
            // nivel de riesgo que un cambio de configuración de módulo (ver
            // CloudStorageSettingsManage/SignatureSettingsManage: assignable por el tenant, pero
            // no otorgado por defecto al empleado). Asignable a un rol custom si el TenantAdmin
            // decide delegarlo (a diferencia de RolesManage/BillingManage/TenantDomainsManage,
            // que son IsAssignableByTenant: false por su riesgo de escalada/facturación).
            new Guid("a1000000-0000-0000-0000-000000000078"),
            ConnectorsAccountsWrite,
            "connectors",
            "Conectar, reconectar y desconectar cuentas de correo del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000079"),
            ScribeTemplatesRead,
            "scribe",
            "Ver templates de correo (System y del tenant)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000080"),
            ScribeTemplatesWrite,
            "scribe",
            "Crear, editar y publicar versiones de templates de correo",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000081"),
            ScribeLayoutsRead,
            "scribe",
            "Ver layouts de correo (System y del tenant)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000082"),
            ScribeLayoutsWrite,
            "scribe",
            "Crear, editar y publicar versiones de layouts de correo",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000083"),
            ScribeEventMappingsRead,
            "scribe",
            "Ver las reglas de resolución evento→template",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000084"),
            ScribeEventMappingsWrite,
            "scribe",
            "Crear, editar y borrar reglas de resolución evento→template",
            false
        ),
        new(
            // Reservado: sin EmailCampaignsController real en Scribe todavía (confirmado por
            // lectura directa de los 4 controllers existentes en la Fase 10.5) — el par
            // campaigns.read/write de ScribePermissions.cs es scaffolding para una feature que
            // aún no se construyó (relacionado con el retiro de EmailCampaigns de Notification,
            // fuera de este plan). Se siembra igual porque el código ya define la constante y este
            // catálogo debe reflejar 1:1 lo que ScribePermissions.cs declara, pero sin otorgarlo
            // por defecto a nadie (ver SystemRoleDefaults) hasta que exista un controller real que
            // lo exija.
            new Guid("a1000000-0000-0000-0000-000000000085"),
            ScribeCampaignsRead,
            "scribe",
            "Ver campañas de correo basadas en templates de Scribe (reservado, sin controller aún)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000086"),
            ScribeCampaignsWrite,
            "scribe",
            "Gestionar campañas de correo basadas en templates de Scribe (reservado, sin controller aún)",
            false
        ),
        new(
            // A diferencia de los 8 permisos anteriores de este bloque, ScribeRender no lo pide
            // ningún endpoint pensado para un humano — RenderController ("POST /scribe/render")
            // solo lo llama Notification vía token de servicio M2M (ScribeRenderClient). Se
            // siembra como fila real únicamente para que ServiceAuth:Clients (Auth) pueda listarlo
            // en el Permissions de un cliente de servicio y que IssueServiceTokenHandler lo emita
            // como claim "perm" en el token. Marcado PlatformOnly (auditoría de aislamiento por
            // tenant_id, 2026-07-18): el comentario original decía que el bundle automático de
            // SystemTenantAdmin era "inofensivo" porque TenantAdmin pasaba HasPermission por rol
            // sin depender de este claim — eso era cierto bajo el bypass de rol que ya se retiró
            // (ver ClaimsPrincipalExtensions). Sin el bypass, un TenantAdmin real SÍ recibe este
            // claim "perm" en su JWT (PermissionCatalog.SystemRoleDefaults) y RenderController
            // toma el TenantId del BODY, no del token (lo necesita así para el caso M2M legítimo:
            // Notification renderiza a nombre de tenants arbitrarios) — sin PlatformOnly, cualquier
            // TenantAdmin podía llamar POST /scribe/render con el TenantId de otro tenant y leer su
            // contenido de template renderizado. PlatformOnly no afecta al caller M2M real: los
            // permisos de un client de servicio vienen de ServiceAuth:Clients (config), no de
            // SystemRoleDefaults — ver Service_token_with_perm_claim_ScribeRender_is_authorized_for_
            // Render en HasPermissionPolicyTests.
            new Guid("a1000000-0000-0000-0000-000000000087"),
            ScribeRender,
            "scribe",
            "Invocar el render de templates (M2M — Notification u otros servicios via token de servicio)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
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
            // Nunca asignable a un rol custom (escalada de billing/límites) NI al rol de sistema
            // Tenant Admin (PlatformOnly): sin caso de uso tenant-propio, es 100% exclusivo de
            // PlatformAdmin (ver SignatureAdminController.UpdateConstraints).
            new Guid("a1000000-0000-0000-0000-000000000088"),
            SignaturePlanConstraintsManage,
            "signature",
            "Gestionar los techos de plan de Signature de un tenant (uso exclusivo de plataforma)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
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
        // Postmaster (auditoría de aislamiento por tenant_id, 2026-07-18 — ver comentario junto a
        // los const de arriba).
        new(
            new Guid("a1000000-0000-0000-0000-000000000089"),
            PostmasterMessagesRead,
            "postmaster",
            "Ver el historial de correos enviados del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000090"),
            PostmasterSuppressionRead,
            "postmaster",
            "Ver la suppression list (direcciones que rebotaron o se dieron de baja) del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000091"),
            PostmasterSuppressionWrite,
            "postmaster",
            "Agregar o quitar direcciones de la suppression list del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000092"),
            PostmasterProvidersRead,
            "postmaster",
            "Ver el proveedor de correo configurado para el tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000093"),
            PostmasterProvidersWrite,
            "postmaster",
            "Configurar el proveedor de correo (SMTP/API) del tenant",
            false
        ),
        // Notification (mismo hallazgo, ver comentario junto a los const de arriba).
        new(
            new Guid("a1000000-0000-0000-0000-000000000094"),
            NotificationSettingsManage,
            "notification",
            "Gestionar la configuración SMTP/API de Notification del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000095"),
            NotificationEmailSend,
            "notification",
            "Enviar un correo puntual desde Notification",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000096"),
            NotificationEmailView,
            "notification",
            "Ver el historial de correos enviados desde Notification",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000097"),
            NotificationTemplateView,
            "notification",
            "Ver los templates de correo del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000098"),
            NotificationTemplateManage,
            "notification",
            "Crear, editar y publicar templates de correo del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000099"),
            NotificationLayoutManage,
            "notification",
            "Gestionar los layouts base de correo del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000100"),
            NotificationCampaignView,
            "notification",
            "Ver campañas de correo del tenant (reservado, sin controller aún)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000101"),
            NotificationCampaignManage,
            "notification",
            "Gestionar campañas de correo del tenant (reservado, sin controller aún)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000102"),
            NotificationLogView,
            "notification",
            "Ver logs de auditoría de Notification del tenant (reservado, sin controller aún)",
            false
        ),
        // PaymentApp (ver comentario junto a los const de arriba).
        new(
            new Guid("a1000000-0000-0000-0000-000000000103"),
            PaymentAppSaaSPaymentRead,
            "payment_app",
            "Ver los pagos SaaS (suscripción/seats/add-ons) del propio tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000104"),
            PaymentAppSaaSPaymentRefund,
            "payment_app",
            "Reembolsar un pago SaaS del propio tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000105"),
            PaymentAppProviderCustomerRead,
            "payment_app",
            "Ver el método de pago guardado (provider customer) del propio tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000106"),
            PaymentAppProviderCustomerManage,
            "payment_app",
            "Gestionar el método de pago guardado del propio tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000107"),
            PaymentAppAdminCrossTenant,
            "payment_app",
            "Ver pagos SaaS de CUALQUIER tenant, incluso suspendido (soporte/investigación, uso exclusivo de plataforma)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        // PaymentClient (ver comentario junto a los const de arriba).
        new(
            new Guid("a1000000-0000-0000-0000-000000000108"),
            PaymentClientConfigRead,
            "payment_client",
            "Ver la configuración de cobro (Stripe DirectApiKeys/Connect) del propio tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000109"),
            PaymentClientConfigManage,
            "payment_client",
            "Configurar el modo/credenciales de cobro del propio tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000110"),
            PaymentClientPaymentRead,
            "payment_client",
            "Ver los pagos que el tenant cobró a sus propios clientes",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000111"),
            PaymentClientPaymentCharge,
            "payment_client",
            "Cobrar un pago a un cliente del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000112"),
            PaymentClientPaymentRefund,
            "payment_client",
            "Reembolsar un pago cobrado a un cliente del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000113"),
            PaymentClientPaymentLinkRead,
            "payment_client",
            "Ver los links de pago del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000114"),
            PaymentClientPaymentLinkManage,
            "payment_client",
            "Crear y gestionar links de pago del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000115"),
            PaymentClientConnectAccountRead,
            "payment_client",
            "Ver el estado de la cuenta Stripe Connect del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000116"),
            PaymentClientConnectAccountOnboard,
            "payment_client",
            "Iniciar el onboarding de la cuenta Stripe Connect del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000117"),
            PaymentClientPayoutRead,
            "payment_client",
            "Ver los payouts programados del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000118"),
            PaymentClientPayoutManage,
            "payment_client",
            "Gestionar el calendario de payouts del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000119"),
            PaymentClientRecurringRead,
            "payment_client",
            "Ver los pagos recurrentes configurados del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000120"),
            PaymentClientRecurringManage,
            "payment_client",
            "Crear y gestionar pagos recurrentes del tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000121"),
            PaymentClientAdminCrossTenant,
            "payment_client",
            "Ver pagos de CUALQUIER tenant, incluso suspendido (soporte/investigación, uso exclusivo de plataforma)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000122"),
            BrandingManage,
            "branding",
            "Gestionar el logo/branding del tenant",
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
            // PlatformOnly se excluye acá — el TenantAdmin nunca lo recibe por defecto, sin
            // importar qué se agregue al catálogo en el futuro (ver Permission.PlatformOnly).
            Role.SystemTenantAdmin => All.Where(definition => !definition.IsCustomerPortal && !definition.PlatformOnly)
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
                // Correspondence: el empleado ve el inbox filtrado por customer de su tenant,
                // puede descargar los adjuntos que aparecen ahí, redactar/responder
                // correspondencia (Fase 11), y enviarla (Fase 14) — mismo criterio operativo que
                // ya cubre el resto de estos permisos, no reservado a TenantAdmin.
                CorrespondenceRead,
                CorrespondenceAttachmentDownload,
                CorrespondenceCompose,
                CorrespondenceReply,
                CorrespondenceSend,
                // Connectors: el empleado puede ver qué cuentas de correo están conectadas (para
                // elegir remitente al redactar correspondencia, o diagnosticar por qué algo no
                // llegó) — no incluye accounts.write (conectar/desconectar es una acción de
                // configuración de integración, reservada a TenantAdmin por defecto, mismo
                // criterio que CloudStorageSettingsManage/SignatureSettingsManage).
                ConnectorsAccountsRead,
                // Scribe: el empleado puede ver los templates/layouts/event-mappings vigentes
                // (System y del tenant) para redactar/diagnosticar comunicaciones — mismo criterio
                // operativo que ConnectorsAccountsRead. No incluye templates.write/layouts.write/
                // event_mappings.write (crear o publicar una versión es un cambio de configuración
                // reservado a TenantAdmin por defecto, mismo criterio que ConnectorsAccountsWrite/
                // CloudStorageSettingsManage/SignatureSettingsManage), ni campaigns.read/write (sin
                // controller real todavía, ver PermissionDefinition), ni scribe.render (M2M-only,
                // nunca un permiso humano — ver PermissionDefinition).
                ScribeTemplatesRead,
                ScribeLayoutsRead,
                ScribeEventMappingsRead,
                // Postmaster: el empleado puede ver el historial de envíos y la suppression list
                // (diagnosticar por qué un correo no llegó) — no incluye providers.write ni
                // suppression.write (configurar el proveedor de correo del tenant o dar de baja
                // una supresión es una acción de configuración, reservada a TenantAdmin por
                // defecto, mismo criterio que ConnectorsAccountsWrite/CloudStorageSettingsManage).
                PostmasterMessagesRead,
                PostmasterSuppressionRead,
                PostmasterProvidersRead,
                // Notification: el empleado consulta templates/layouts vigentes y el historial de
                // envíos para diagnosticar — no incluye template.manage/layout.manage/
                // settings.manage (cambios de configuración, reservados a TenantAdmin) ni
                // campaign.view/manage (sin controller real todavía, ver PermissionDefinition).
                NotificationEmailView,
                NotificationTemplateView,
                // PaymentApp/PaymentClient: el empleado consulta pagos/config/links/payouts/
                // recurrentes del propio tenant para atender consultas de clientes — no incluye
                // refund/charge/manage/onboard (mover dinero o cambiar configuración de cobro es
                // una acción reservada a TenantAdmin por defecto, mismo criterio que
                // ConnectorsAccountsWrite/CloudStorageSettingsManage) ni admin.cross_tenant
                // (PlatformOnly, ni siquiera TenantAdmin lo recibe).
                PaymentAppSaaSPaymentRead,
                PaymentAppProviderCustomerRead,
                PaymentClientConfigRead,
                PaymentClientPaymentRead,
                PaymentClientPaymentLinkRead,
                PaymentClientConnectAccountRead,
                PaymentClientPayoutRead,
                PaymentClientRecurringRead,
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
