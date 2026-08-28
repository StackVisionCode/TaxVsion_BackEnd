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
    public const string BrandingManage = TenantBrandingPermissions.Manage;
    public const string PlatformBrandingManage = TenantBrandingPermissions.Platform;

    // Módulos operativos
    public const string CustomersView = CustomersPermissions.View;
    public const string CustomersManage = CustomersPermissions.Manage;
    public const string CustomersFiscalProfileReveal = CustomersPermissions.FiscalProfileReveal;
    public const string CustomersPreparerManage = CustomersPermissions.PreparerManage;
    public const string SignaturesRequest = "signatures.request";
    public const string DocumentsView = "documents.view";
    public const string DocumentsManage = "documents.manage";
    public const string DocumentsBrandingManage = DocumentsPermissions.BrandingManage;
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
    public const string SignatureRequestManage = SignaturePermissions.RequestManage;
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

    // SMS — envío de SMS/MMS agnóstico de proveedor (bounded context propio, microservicio Sms).
    // Lo exige "POST /sms/messages" vía [HasPermission(SmsPermissions.Send)]. A diferencia de
    // ScribeRender, sí lo reciben roles humanos (TenantAdmin/TenantEmployee) además del caller M2M
    // (un microservicio que envía SMS lo lleva como claim "perm" vía ServiceAuth:Clients de Auth).
    public const string SmsSend = SmsPermissions.Send;

    // Catalog — productos/servicios/categorías (microservicio Catalog). Humano-asignables: TenantAdmin
    // los recibe vía SystemRoleDefaults; los callers M2M los llevan como claim "perm" (ServiceAuth:Clients).
    public const string CatalogRead = CatalogPermissions.Read;
    public const string CatalogWrite = CatalogPermissions.Write;
    public const string CatalogDelete = CatalogPermissions.Delete;

    // Inventory — stock/proveedores/movimientos (microservicio Inventory). Humano-asignables (TenantAdmin
    // vía defaults); los callers M2M los llevan como claim "perm" (ServiceAuth:Clients).
    public const string InventoryRead = InventoryPermissions.Read;
    public const string InventoryWrite = InventoryPermissions.Write;
    public const string InventoryAdjust = InventoryPermissions.Adjust;

    // Postmaster — envío/entrega de correo, proveedores por tenant y suppression list (bounded
    // context propio, ver microservicio Postmaster). Estos 5 permisos ya los exigían los 3
    // controllers de Postmaster vía [HasPermission(...)], pero nunca se habían sembrado en este
    // catálogo. ProvidersWrite cubre también PUT /postmaster/system/provider/{code} (el proveedor
    // default de plataforma); ese endpoint ya trae su propio
    // [AllowActorTypes(ActorType.PlatformAdmin)] — no hace falta PlatformOnly aquí.
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

    // Notes — notas internas/portal sobre customers y otras entidades (bounded context propio,
    // ver microservicio Notes). Read/Manage son el uso normal de staff (Manage exige además ser
    // el autor, chequeado en Application — ver ADR-06); ViewAll es gobernanza: un TenantAdmin
    // puede leer/archivar/borrar notas ajenas, pero NUNCA editar su contenido (no hay override de
    // Manage). PortalRead es exclusivo del cliente final leyendo sus propias notas ClientVisible.
    // Reminder — recordatorios personales sobre tareas, eventos, notas o sueltos (bounded context
    // propio, microservicio Reminder). Un recordatorio pertenece siempre a un usuario del tenant,
    // así que no hay variante de portal ni permiso de gobernanza: nadie ve ni edita recordatorios
    // ajenos, y ese filtro por UserId lo aplica el handler, no el permiso.
    public const string RemindersRead = ReminderPermissions.Read;
    public const string RemindersWrite = ReminderPermissions.Write;

    public const string NotesRead = NotesPermissions.Read;
    public const string NotesManage = NotesPermissions.Manage;
    public const string NotesViewAll = NotesPermissions.ViewAll;
    public const string NotesPortalRead = NotesPermissions.PortalRead;

    // Task — trabajo interno de la firma (bounded context propio, microservicio Task). A diferencia
    // de Reminder, acá SÍ hay gobernanza: ManageAll es el override del supervisor que cierra o
    // reasigna la tarea de otro. Assign existe aparte de Write porque poner trabajo en la bandeja
    // ajena no es lo mismo que crear el propio. Sin variante de portal: el cliente final nunca ve
    // la lista de tareas — lo que le llega sale por Notification.
    public const string TasksRead = TasksPermissions.Read;
    public const string TasksWrite = TasksPermissions.Write;
    public const string TasksAssign = TasksPermissions.Assign;
    public const string TasksManageAll = TasksPermissions.ManageAll;
    public const string TasksTemplatesManage = TasksPermissions.TemplatesManage;
    public const string TasksClientRequestsManage = TasksPermissions.ClientRequestsManage;
    public const string TasksPortalClientRequests = TasksPermissions.PortalClientRequests;

    public const string CalendarRead = CalendarPermissions.Read;
    public const string CalendarWrite = CalendarPermissions.Write;
    public const string CalendarManageAll = CalendarPermissions.ManageAll;
    public const string CalendarTypesManage = CalendarPermissions.TypesManage;
    public const string CalendarAvailabilityManage = CalendarPermissions.AvailabilityManage;

    // Portal del cliente final
    public const string PortalCallsUse = "portal.calls.use";
    public const string PortalMilesUse = "portal.miles.use";
    public const string PortalFoldersView = "portal.folders.view";

    // Communication — chat, llamadas, meetings (bounded context propio, ver microservicio
    // Communication). Los 18 GUID/Code de abajo YA existen como filas reales en la tabla
    // Permissions (sembradas por SQL directo en la migración AddCommunicationPermissions) —
    // se reconcilian aquí con los MISMOS GUID exactos; la migración que agrega
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
    // AdminCrossTenant (ambos) es PlatformOnly: true — su propio controller
    // (PaymentAppAdminController / PaymentClientAdminController) documenta que el tenant es un
    // filtro OPCIONAL, no una restricción, así que sin PlatformOnly cualquier TenantAdmin vería
    // pagos de cualquier otro tenant por defecto.
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

    // Subscription — RBAC Fase 8 (RBAC_Hardening_Plan.md): migración de [Authorize(Roles=...)] a
    // [HasPermission]. PlanChange cubre el ciclo de vida TenantAdmin-only de la suscripción base
    // del propio tenant (change-plan/activate/cancel/cancel-pending-plan-change en
    // SubscriptionsController) — IsAssignableByTenant:false (billing-adjacent, mismo criterio que
    // SubscriptionManage/BillingView) pero deliberadamente NO IsDangerous: el rol de sistema
    // TenantAdmin ya lo tenía sin restricción vía Roles="TenantAdmin", migrarlo a IsDangerous lo
    // sacaría del bundle automático y sería una regresión real (el plan exige "más permisivo, no
    // bloquea injustamente"). Suspend/Reactivate/Renew son operaciones administrativas de
    // plataforma sobre CUALQUIER tenant (antes Roles="PlatformAdmin") — PlatformOnly:true, el
    // propio bypass de PlatformAdmin en ProjectionPermissionsSource las cubre sin necesidad de
    // que entren al bundle de nadie. AdminCrossTenant cubre las 4 consultas cross-tenant de
    // Admin/AdminController (antes Roles="PlatformAdmin" a nivel de clase), mismo criterio que
    // GrowthAdminCrossTenant/PaymentAppAdminCrossTenant. SeatsManage/AddOnsManage cubren
    // SeatsController y AddOnsController completos (antes Roles="TenantAdmin" en ambos) — mismo
    // criterio IsAssignableByTenant:false/no-IsDangerous que PlanChange. AuditController reusa el
    // audit.view genérico ya existente (antes Roles="TenantAdmin,PlatformAdmin"), no necesita
    // permiso nuevo.
    public const string SubscriptionPlanChange = SubscriptionPermissions.PlanChange;
    public const string SubscriptionSuspend = SubscriptionPermissions.Suspend;
    public const string SubscriptionReactivate = SubscriptionPermissions.Reactivate;
    public const string SubscriptionRenew = SubscriptionPermissions.Renew;
    public const string SubscriptionAdminCrossTenant = SubscriptionPermissions.AdminCrossTenant;
    public const string SeatsManage = SubscriptionPermissions.SeatsManage;
    public const string AddOnsManage = SubscriptionPermissions.AddOnsManage;

    // Tenant — RBAC Fase 8: TenantController.Get (listado cross-tenant) y ChangeStatus (antes
    // ambos Roles="PlatformAdmin") — PlatformOnly:true, mismo criterio que Subscription arriba.
    // Create no se toca (ya usa [Authorize(Policy = "TenantRegistration")] +
    // [AuthorizedByCapabilityToken], un mecanismo de Capa 3 distinto y deliberado).
    public const string TenantStatusChange = TenantPermissions.StatusChange;
    public const string TenantListView = TenantPermissions.ListView;

    // PayFlow (Fase 17) — OnboardingAdminController: listar/inspeccionar onboardings en
    // ManualReview/ProvisioningFailed y actuar sobre ellos (resume/update-and-resume/force-complete/
    // cancel-and-refund) de CUALQUIER tenant en curso — el tenant todavía no existe en la mayoría de
    // los casos, así que "cross-tenant" ni siquiera aplica: es inherentemente PlatformOnly.
    public const string OnboardingAdminManage = "onboarding.admin.manage";

    // Growth — Codes y Referrals comparten deployment, pero conservan permisos de dominio
    // separados. AdminCrossTenant nunca se asigna a roles de tenant.
    public const string GrowthCodesRead = GrowthPermissions.CodesRead;
    public const string GrowthCodesManage = GrowthPermissions.CodesManage;
    public const string GrowthCodesIssue = GrowthPermissions.CodesIssue;
    public const string GrowthCodesActivate = GrowthPermissions.CodesActivate;
    public const string GrowthCodesRevoke = GrowthPermissions.CodesRevoke;
    public const string GrowthCodesAuditRead = GrowthPermissions.CodesAuditRead;
    public const string GrowthCodesRedemptionRead = GrowthPermissions.CodesRedemptionRead;
    public const string GrowthCodesCompensationManage = GrowthPermissions.CodesCompensationManage;
    public const string GrowthReferralsOwnRead = GrowthPermissions.ReferralsOwnRead;
    public const string GrowthReferralsProgramRead = GrowthPermissions.ReferralsProgramRead;
    public const string GrowthReferralsProgramManage = GrowthPermissions.ReferralsProgramManage;
    public const string GrowthReferralsAttributionRead = GrowthPermissions.ReferralsAttributionRead;
    public const string GrowthReferralsFraudRead = GrowthPermissions.ReferralsFraudRead;
    public const string GrowthReferralsFraudManage = GrowthPermissions.ReferralsFraudManage;
    public const string GrowthReferralsRewardRead = GrowthPermissions.ReferralsRewardRead;
    public const string GrowthReferralsRewardManage = GrowthPermissions.ReferralsRewardManage;
    public const string GrowthReferralsAuditRead = GrowthPermissions.ReferralsAuditRead;
    public const string GrowthAdminCrossTenant = GrowthPermissions.AdminCrossTenant;

    public sealed record PermissionDefinition(
        Guid Id,
        string Code,
        string Module,
        string Description,
        bool IsCustomerPortal,
        int MinPlanTier = (int)PlanTier.Starter,
        bool IsAssignableByTenant = true,
        bool PlatformOnly = false,
        // Explícito solo cuando la inferencia por defecto (ver Permission.InferAllowedActorTypes)
        // no alcanza — Fase 7 del plan anota permiso por permiso, no hace falta tocar los ~140 ya
        // sembrados de una sola vez.
        UserActorType[]? AllowedActorTypes = null,
        // RBAC Fase 2 (RBAC_Hardening_Plan.md): si es true, el rol de sistema "Tenant Admin"
        // NUNCA lo incluye por defecto, sin importar que IsCustomerPortal/PlatformOnly sean
        // false — distinto de PlatformOnly (que ya lo excluye) porque estos SÍ tienen un caso de
        // uso legítimo para un tenant, pero son de riesgo alto (auto-escalada, financiero, legal,
        // lock-out) y deben entrar por asignación explícita, no por el bundle automático. Ver
        // SystemRoleDefaults(SystemTenantAdmin) más abajo.
        bool IsDangerous = false
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
            // RBAC Fase 2: IsDangerous — auto-escalada, no debe venir por default en TenantAdmin.
            new Guid("a1000000-0000-0000-0000-000000000004"),
            RolesManage,
            "users",
            "Gestionar roles y permisos",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            IsAssignableByTenant: false,
            IsDangerous: true
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
            // RBAC Fase 2: IsDangerous — financiero, no debe venir por default en TenantAdmin.
            // Sin efecto funcional hoy: Subscription (único consumidor conceptual de billing.*)
            // todavía usa 100% [Authorize(Roles="TenantAdmin")], no [HasPermission] — ver README
            // §41. El día que migre, este permiso ya exige asignación explícita, no automática.
            new Guid("a1000000-0000-0000-0000-000000000007"),
            BillingView,
            "billing",
            "Ver facturación y suscripción",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            IsAssignableByTenant: false,
            IsDangerous: true
        ),
        new(
            // RBAC Fase 2: IsDangerous — ver nota de BillingView (mismo caso).
            new Guid("a1000000-0000-0000-0000-000000000008"),
            BillingManage,
            "billing",
            "Gestionar métodos de pago y facturación",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            IsAssignableByTenant: false,
            IsDangerous: true
        ),
        new(
            // Reservado: incluye compra/baja de asientos — impacta directamente la facturación.
            // RBAC Fase 2: IsDangerous — ver nota de BillingView (mismo caso, mismo consumidor
            // conceptual sin migrar a [HasPermission] todavía).
            new Guid("a1000000-0000-0000-0000-000000000009"),
            SubscriptionManage,
            "billing",
            "Cambiar plan y gestionar suscripción",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            IsAssignableByTenant: false,
            IsDangerous: true
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
            new Guid("a1000000-0000-0000-0000-000000000152"),
            DocumentsBrandingManage,
            "documents",
            "Configurar el branding de documentos del tenant",
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
            // Explícito (no inferido): SystemRoleDefaults(SystemCustomerPortal) ya le otorga
            // este permiso al rol Customer Portal sembrado en cada tenant — un cliente real
            // sube/ve/descarga sus propios archivos hoy. IsCustomerPortal queda en false porque
            // el permiso NO es exclusivo de cliente (staff también lo usa) — InferAllowedActorTypes
            // no modela "compartido", así que hace falta declarar el AllowedActorTypes real acá.
            // El scope por customer_id (quién ve QUÉ archivo) lo resuelve StorageIdentity.cs, no
            // esta capa (ver Actor_Type_Authorization_Layers_Plan.md §4.1).
            new Guid("a1000000-0000-0000-0000-000000000023"),
            CloudStorageFileView,
            "cloudstorage",
            "Ver metadatos de archivos",
            false,
            AllowedActorTypes:
            [
                UserActorType.TenantEmployee,
                UserActorType.TenantAdmin,
                UserActorType.PlatformAdmin,
                UserActorType.CustomerPortal,
            ]
        ),
        new(
            // Ver nota de CloudStorageFileView — mismo caso: rol Customer Portal ya lo tiene.
            new Guid("a1000000-0000-0000-0000-000000000024"),
            CloudStorageFileUpload,
            "cloudstorage",
            "Subir archivos mediante el gateway seguro",
            false,
            AllowedActorTypes:
            [
                UserActorType.TenantEmployee,
                UserActorType.TenantAdmin,
                UserActorType.PlatformAdmin,
                UserActorType.CustomerPortal,
            ]
        ),
        new(
            // Ver nota de CloudStorageFileView — mismo caso: rol Customer Portal ya lo tiene.
            new Guid("a1000000-0000-0000-0000-000000000025"),
            CloudStorageFileDownload,
            "cloudstorage",
            "Descargar archivos disponibles",
            false,
            AllowedActorTypes:
            [
                UserActorType.TenantEmployee,
                UserActorType.TenantAdmin,
                UserActorType.PlatformAdmin,
                UserActorType.CustomerPortal,
            ]
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
            // RBAC Fase 2: IsDangerous — este es el bug real que motivó la fase.
            // LegalController.RegisterTakedown solo exige [HasPermission(LegalManage)] (el
            // AllowActorTypes de clase incluye TenantEmployee/TenantAdmin, no solo
            // PlatformAdmin), así que sin IsDangerous cualquier TenantAdmin podía registrar un
            // legal hold sobre archivos de SU PROPIO tenant pese a que el comentario de arriba
            // ya decía "nunca de un tenant" — la intención nunca se aplicó en runtime.
            new Guid("a1000000-0000-0000-0000-000000000070"),
            CloudStorageLegalManage,
            "cloudstorage",
            "Gestionar legal hold y takedowns DMCA",
            false,
            IsAssignableByTenant: false,
            IsDangerous: true
        ),
        new(
            // A diferencia de LegalManage, esto lo ejerce el propio tenant sobre
            // sus archivos (responder a un takedown recibido) — mismo nivel de
            // TenantAdmin-only que CloudStorageFileDelete, no de plataforma.
            // RBAC Fase 2: deliberadamente NO marcado IsDangerous, a diferencia de lo que sugería
            // el plan original — ver LegalController.SubmitCounterNotice: es la respuesta legal
            // propia del tenant a un takedown recibido sobre SU archivo, con plazos legales reales
            // (17 U.S.C. §512(g), ventana de 10-14 días hábiles). Quitarlo del default de
            // TenantAdmin dejaría a la oficina sin forma de auto-defenderse ante un DMCA sin
            // depender de PlatformAdmin — justo lo opuesto de lo que dice este mismo comentario
            // ("lo ejerce el propio tenant").
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
            // como claim "perm" en el token. Marcado PlatformOnly: un TenantAdmin real recibe este
            // claim vía SystemRoleDefaults, y RenderController toma el TenantId del BODY (no del
            // token, para soportar el caso M2M legítimo de renderizar a nombre de tenants
            // arbitrarios) — sin PlatformOnly, cualquier TenantAdmin podía llamar POST
            // /scribe/render con el TenantId de otro tenant y leer su contenido renderizado.
            // PlatformOnly no afecta al caller M2M real: los permisos de un client de servicio
            // vienen de ServiceAuth:Clients (config), no de SystemRoleDefaults.
            new Guid("a1000000-0000-0000-0000-000000000087"),
            ScribeRender,
            "scribe",
            "Invocar el render de templates (M2M — Notification u otros servicios via token de servicio)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        // SMS — a diferencia de ScribeRender, sí es un permiso humano-asignable: un TenantAdmin lo
        // recibe vía SystemRoleDefaults (no CustomerPortal, no PlatformOnly, no Dangerous), y el
        // caller M2M lo lleva como claim "perm" vía ServiceAuth:Clients (config, no rol). El
        // endpoint "POST /sms/messages" toma el TenantId del TOKEN (no del body), así que no aplica
        // el riesgo cross-tenant que obligó a marcar ScribeRender como PlatformOnly.
        new(
            new Guid("a1000000-0000-0000-0000-000000000158"),
            SmsSend,
            "sms",
            "Enviar SMS/MMS (batch 1..N) vía el microservicio SMS",
            false
        ),
        // Catalog — productos/servicios/categorías. Humano-asignables (TenantAdmin vía defaults).
        new(
            new Guid("a1000000-0000-0000-0000-000000000159"),
            CatalogRead,
            "catalog",
            "Ver el catálogo de productos/servicios",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000160"),
            CatalogWrite,
            "catalog",
            "Crear/editar productos, servicios y categorías",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000161"),
            CatalogDelete,
            "catalog",
            "Borrar productos, servicios y categorías",
            false
        ),
        // Inventory
        new(
            new Guid("a1000000-0000-0000-0000-000000000162"),
            InventoryRead,
            "inventory",
            "Ver stock, proveedores y movimientos",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000163"),
            InventoryWrite,
            "inventory",
            "Gestionar proveedores y umbrales de stock",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000164"),
            InventoryAdjust,
            "inventory",
            "Ajustar stock (registrar movimientos)",
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
            // RBAC Fase 4 — override de ownership: enviar/cancelar/extender solicitudes creadas
            // por OTRO usuario del tenant (por default, IsOwnerOrHasManageHandler solo deja
            // operar al creador). Mismo criterio que CloudStorageShareManage (...0069):
            // IsAssignableByTenant: false, un TenantAdmin no puede otorgárselo a un rol custom
            // libremente — solo llega vía SystemRoleDefaults.
            new Guid("a1000000-0000-0000-0000-000000000142"),
            SignatureRequestManage,
            "signature",
            "Gestionar solicitudes de firma creadas por otros usuarios del tenant",
            false,
            IsAssignableByTenant: false
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
            // RBAC Fase 2: IsDangerous acá es redundante (PlatformOnly ya lo excluye de
            // SystemRoleDefaults(SystemTenantAdmin)) — se marca igual por consistencia
            // documental con el resto de la lista IsDangerous del plan.
            new Guid("a1000000-0000-0000-0000-000000000088"),
            SignaturePlanConstraintsManage,
            "signature",
            "Gestionar los techos de plan de Signature de un tenant (uso exclusivo de plataforma)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true,
            IsDangerous: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000063"),
            CustomersFiscalProfileReveal,
            "customers",
            "Revelar el SSN/ITIN/EIN completo de un customer",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000141"),
            CustomersPreparerManage,
            "customers",
            "Asignar o reasignar el preparador responsable de un customer",
            false
        ),
        new(
            // Reservado: quien agrega/deshabilita dominios controla qué Host puede
            // autenticar como este tenant (Fase A5) — riesgo equivalente a
            // RolesManage/BillingManage. Nunca asignable a un rol custom.
            // RBAC Fase 2: IsDangerous — cambiar el subdominio impacta el login de TODOS los
            // usuarios del tenant de una sola vez, no es una acción operativa diaria.
            new Guid("a1000000-0000-0000-0000-000000000064"),
            TenantDomainsManage,
            "domains",
            "Gestionar dominios propios del tenant (custom hostnames)",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            IsAssignableByTenant: false,
            IsDangerous: true
        ),
        // --- Communication (reconciliado, ver comentario arriba) ---
        new(
            // Explícito (no inferido): SystemRoleDefaults(SystemEmployee) Y
            // SystemRoleDefaults(SystemCustomerPortal) otorgan este permiso por defecto —
            // staff Y cliente lo usan hoy (mismo hallazgo que CloudStorageFileView/Upload/
            // Download en Fase 4: IsCustomerPortal=true infería CustomerPortal-only, pero un
            // TenantEmployee real también lo tiene vía el rol de sistema "Employee". Sin este
            // fix, ActorTypeRoleGuard rechaza la propia asignación del rol "Employee" a
            // cualquier TenantEmployee — encontrado en Fase 7 (catalogación explícita), antes
            // de que llegara a producción).
            new Guid("a1000000-0000-0000-0000-000000000045"),
            CommunicationChatStart,
            "communication",
            "Iniciar conversaciones de chat",
            true,
            MinPlanTier: (int)PlanTier.Pro,
            AllowedActorTypes:
            [
                UserActorType.TenantEmployee,
                UserActorType.TenantAdmin,
                UserActorType.PlatformAdmin,
                UserActorType.CustomerPortal,
            ]
        ),
        new(
            // Ver nota de CommunicationChatStart — mismo caso.
            new Guid("a1000000-0000-0000-0000-000000000046"),
            CommunicationChatReply,
            "communication",
            "Responder en conversaciones de chat",
            true,
            MinPlanTier: (int)PlanTier.Pro,
            AllowedActorTypes:
            [
                UserActorType.TenantEmployee,
                UserActorType.TenantAdmin,
                UserActorType.PlatformAdmin,
                UserActorType.CustomerPortal,
            ]
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
            // Explícito (no inferido): a diferencia de ChatStart/ChatReply arriba, este tiene
            // IsCustomerPortal=false (infiere staff-only), pero SystemRoleDefaults
            // (SystemCustomerPortal) también lo otorga — el cliente abre su propio chat de
            // soporte hacia el PlatformTenant. Mismo bug, sentido inverso (mismo hallazgo de
            // Fase 7 que ChatStart/ChatReply arriba).
            new Guid("a1000000-0000-0000-0000-000000000048"),
            CommunicationSupportOpen,
            "communication",
            "Abrir chat de soporte hacia el PlatformTenant",
            false,
            MinPlanTier: (int)PlanTier.Pro,
            AllowedActorTypes:
            [
                UserActorType.TenantEmployee,
                UserActorType.TenantAdmin,
                UserActorType.PlatformAdmin,
                UserActorType.CustomerPortal,
            ]
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
            // Ver nota de CommunicationChatStart — mismo caso (staff Y cliente lo tienen hoy).
            new Guid("a1000000-0000-0000-0000-000000000054"),
            CommunicationMeetingJoin,
            "communication",
            "Unirse a reuniones (previa invitación válida)",
            true,
            MinPlanTier: (int)PlanTier.Pro,
            AllowedActorTypes:
            [
                UserActorType.TenantEmployee,
                UserActorType.TenantAdmin,
                UserActorType.PlatformAdmin,
                UserActorType.CustomerPortal,
            ]
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
            // Ver nota de CommunicationChatStart — mismo caso (staff Y cliente lo tienen hoy).
            new Guid("a1000000-0000-0000-0000-000000000057"),
            CommunicationScreenshotCreate,
            "communication",
            "Adjuntar screenshots/voice/video en chat",
            true,
            MinPlanTier: (int)PlanTier.Pro,
            AllowedActorTypes:
            [
                UserActorType.TenantEmployee,
                UserActorType.TenantAdmin,
                UserActorType.PlatformAdmin,
                UserActorType.CustomerPortal,
            ]
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
            // Ver nota de CommunicationChatStart — mismo caso (staff Y cliente lo tienen hoy).
            new Guid("a1000000-0000-0000-0000-000000000060"),
            CommunicationNotificationRead,
            "communication",
            "Consultar notificaciones in-app propias",
            true,
            MinPlanTier: (int)PlanTier.Pro,
            AllowedActorTypes:
            [
                UserActorType.TenantEmployee,
                UserActorType.TenantAdmin,
                UserActorType.PlatformAdmin,
                UserActorType.CustomerPortal,
            ]
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
        // Postmaster (ver comentario junto a los const de arriba).
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
        new(
            new Guid("a1000000-0000-0000-0000-000000000123"),
            GrowthCodesRead,
            "codes",
            "Ver códigos del propio tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000124"),
            GrowthCodesManage,
            "codes",
            "Gestionar códigos del propio tenant",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000125"),
            GrowthCodesIssue,
            "codes",
            "Emitir códigos de beneficio",
            false
        ),
        new(new Guid("a1000000-0000-0000-0000-000000000126"), GrowthCodesActivate, "codes", "Activar códigos", false),
        new(new Guid("a1000000-0000-0000-0000-000000000127"), GrowthCodesRevoke, "codes", "Revocar códigos", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000128"),
            GrowthCodesAuditRead,
            "codes",
            "Consultar auditoría de códigos",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000129"),
            GrowthCodesRedemptionRead,
            "codes",
            "Consultar redemptions",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000130"),
            GrowthCodesCompensationManage,
            "codes",
            "Gestionar compensaciones promocionales",
            false,
            IsAssignableByTenant: false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000131"),
            GrowthReferralsOwnRead,
            "referrals",
            "Ver referidos propios",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000132"),
            GrowthReferralsProgramRead,
            "referrals",
            "Ver programas de referidos",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000133"),
            GrowthReferralsProgramManage,
            "referrals",
            "Gestionar programas de referidos",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000134"),
            GrowthReferralsAttributionRead,
            "referrals",
            "Consultar atribuciones",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000135"),
            GrowthReferralsFraudRead,
            "referrals",
            "Consultar revisiones antifraude",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000136"),
            GrowthReferralsFraudManage,
            "referrals",
            "Gestionar revisiones antifraude",
            false,
            IsAssignableByTenant: false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000137"),
            GrowthReferralsRewardRead,
            "referrals",
            "Consultar rewards",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000138"),
            GrowthReferralsRewardManage,
            "referrals",
            "Gestionar rewards no monetarios",
            false,
            IsAssignableByTenant: false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000139"),
            GrowthReferralsAuditRead,
            "referrals",
            "Consultar auditoría de referidos",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000140"),
            GrowthAdminCrossTenant,
            "growth",
            "Operar recursos Growth de cualquier tenant",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        // --- Subscription (RBAC Fase 8, ver comentario junto a los const de arriba) ---
        new(
            new Guid("a1000000-0000-0000-0000-000000000143"),
            SubscriptionPlanChange,
            "subscription",
            "Cambiar plan, activar, cancelar y gestionar el ciclo de vida de la suscripción del propio tenant",
            false,
            IsAssignableByTenant: false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000144"),
            SubscriptionSuspend,
            "subscription",
            "Suspender la suscripción de cualquier tenant (uso exclusivo de plataforma)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000145"),
            SubscriptionReactivate,
            "subscription",
            "Reactivar la suscripción de cualquier tenant (uso exclusivo de plataforma)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000146"),
            SubscriptionRenew,
            "subscription",
            "Renovación manual de la suscripción de cualquier tenant, mientras no exista Billing (uso exclusivo de plataforma)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000147"),
            SubscriptionAdminCrossTenant,
            "subscription",
            "Consultar renovaciones próximas, seats vencidos y suscripciones en mora de CUALQUIER tenant, y forzar el recálculo de entitlements (uso exclusivo de plataforma)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000148"),
            SeatsManage,
            "seats",
            "Comprar, asignar, liberar, reasignar y renovar seats del propio tenant",
            false,
            IsAssignableByTenant: false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000149"),
            AddOnsManage,
            "addons",
            "Comprar, cancelar y renovar add-ons del propio tenant",
            false,
            IsAssignableByTenant: false
        ),
        // --- Tenant (RBAC Fase 8, ver comentario junto a los const de arriba) ---
        new(
            new Guid("a1000000-0000-0000-0000-000000000150"),
            TenantStatusChange,
            "tenant",
            "Cambiar el estado de cualquier tenant (uso exclusivo de plataforma)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000151"),
            TenantListView,
            "tenant",
            "Listar todos los tenants de la plataforma (uso exclusivo de plataforma)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000153"),
            OnboardingAdminManage,
            "onboarding",
            "Ver y administrar onboardings de PayFlow en ManualReview/ProvisioningFailed de cualquier tenant (resume, corrección, force-complete, cancelar y reembolsar)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(new Guid("a1000000-0000-0000-0000-000000000154"), NotesRead, "notes", "Ver notas del tenant", false),
        new(
            // ADR-06: Manage cubre crear/editar/pin/color/visibilidad/adjuntar — la regla "solo
            // el propio autor" NO vive acá (Permission no modela ownership), la aplica el handler
            // (note.CreatedByUserId == actorUserId) en Application, igual que Correspondence Draft.
            new Guid("a1000000-0000-0000-0000-000000000155"),
            NotesManage,
            "notes",
            "Crear, editar, archivar/restaurar y adjuntar archivos a notas propias",
            false
        ),
        new(
            // Gobernanza (ADR-06): un TenantAdmin/PlatformAdmin puede leer, archivar o borrar
            // notas de CUALQUIER autor del tenant — nunca editar su contenido (eso exige ser el
            // autor vía NotesManage). Explícitamente sin TenantEmployee: leer notas ajenas no es
            // parte del bundle por defecto de un empleado.
            new Guid("a1000000-0000-0000-0000-000000000156"),
            NotesViewAll,
            "notes",
            "Ver, archivar y borrar notas de cualquier autor del tenant (gobernanza)",
            false,
            AllowedActorTypes: [UserActorType.TenantAdmin, UserActorType.PlatformAdmin]
        ),
        new(
            // IsCustomerPortal:true → InferAllowedActorTypes ya limita esto a [CustomerPortal]
            // (ver Permission.InferAllowedActorTypes) — el cliente final solo ve sus propias
            // notas con Visibility=ClientVisible, filtro que aplica el handler, no este permiso.
            new Guid("a1000000-0000-0000-0000-000000000157"),
            NotesPortalRead,
            "notes",
            "El cliente puede ver sus notas marcadas como visibles para el cliente",
            true
        ),
        // Reminder — sin AllowedActorTypes explícito a propósito: la inferencia por defecto de
        // Permission da [TenantEmployee, TenantAdmin, PlatformAdmin], que es exactamente lo que
        // pide el diseño. Marcarlo a mano sería duplicar la regla y arriesgarse a que se desincronice.
        new(
            new Guid("a1000000-0000-0000-0000-000000000165"),
            RemindersRead,
            "reminders",
            "Ver los recordatorios propios",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000166"),
            RemindersWrite,
            "reminders",
            "Crear, reprogramar, posponer, descartar y cancelar recordatorios propios",
            false
        ),
        // Task — los cinco sin AllowedActorTypes explícito, incluido ManageAll. La inferencia por
        // defecto da [TenantEmployee, TenantAdmin, PlatformAdmin] y eso es lo correcto acá, a
        // diferencia de NotesViewAll (que sí excluye a TenantEmployee): en una firma fiscal el
        // supervisor que revisa y desatasca es normalmente un preparador senior, no el admin del
        // tenant. Restringirlo a TenantAdmin dejaría al override sin poder otorgarse nunca a quien
        // de verdad lo ejerce. Lo que sí se hace es dejarlo FUERA del bundle por defecto del
        // empleado: se otorga por rol explícito.
        new(new Guid("a1000000-0000-0000-0000-000000000167"), TasksRead, "tasks", "Ver las tareas del tenant", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000168"),
            TasksWrite,
            "tasks",
            "Crear, editar, cerrar y reabrir tareas propias o asignadas a uno mismo",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000169"),
            TasksAssign,
            "tasks",
            "Asignar una tarea a otra persona del tenant (sin restricción de dirección)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000170"),
            TasksManageAll,
            "tasks",
            "Cerrar, editar o reasignar la tarea de cualquier usuario del tenant (supervisión)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000171"),
            TasksTemplatesManage,
            "tasks",
            "Crear y editar las plantillas de tarea de la firma",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000172"),
            TasksClientRequestsManage,
            "tasks",
            "Pedirle documentacion al cliente y cerrar lo que mande",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000174"),
            CalendarRead,
            "calendar",
            "Ver el calendario del tenant y consultar disponibilidad",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000175"),
            CalendarWrite,
            "calendar",
            "Crear, mover y cancelar las citas propias",
            false
        ),
        // No anula ADR-C-09: el agregado sigue exigiendo organizador. Permite actuar como tal.
        new(
            new Guid("a1000000-0000-0000-0000-000000000176"),
            CalendarManageAll,
            "calendar",
            "Reorganizar agendas ajenas actuando como organizador (supervision)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000177"),
            CalendarTypesManage,
            "calendar",
            "Definir los tipos de cita de la firma",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000178"),
            CalendarAvailabilityManage,
            "calendar",
            "Definir horarios de atencion y bloqueos de agenda",
            false
        ),
        // El unico de este modulo cuyo destinatario esta fuera de la firma: el cliente ve su lista
        // de pedidos, no la tarea interna de la que salieron.
        new(
            new Guid("a1000000-0000-0000-0000-000000000173"),
            TasksPortalClientRequests,
            "tasks",
            "El cliente ve sus pedidos y registra lo que sube",
            true
        ),
        // Marca del SISTEMA: solo PlatformAdmin. PlatformOnly excluye este permiso del bundle del
        // rol "Tenant Admin"; IsAssignableByTenant: false impide que un rol custom lo incluya.
        new(
            new Guid("a1000000-0000-0000-0000-000000000179"),
            PlatformBrandingManage,
            "branding",
            "Gestionar la marca del sistema (colores/logo/favicon por defecto de la plataforma)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
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
            // RBAC Fase 2: IsDangerous también se excluye — a diferencia de PlatformOnly (sin
            // caso de uso tenant-propio), estos SÍ tienen un caso de uso legítimo para un
            // TenantAdmin, pero de riesgo alto (auto-escalada/financiero/legal/lock-out) y deben
            // entrar por asignación explícita, no por el bundle automático. Antes de esta fase,
            // un permiso nuevo con IsCustomerPortal:false/PlatformOnly:false entraba
            // automáticamente al set del TenantAdmin sin importar su riesgo real.
            Role.SystemTenantAdmin => All.Where(definition =>
                    !definition.IsCustomerPortal && !definition.PlatformOnly && !definition.IsDangerous
                )
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
                // Reminder sí entra en el bundle por defecto del empleado, a diferencia de Notes:
                // un recordatorio es del propio usuario (Reminder.UserId), no un recurso compartido
                // del tenant. Sin estos dos permisos un empleado no podría ni crearse un
                // recordatorio propio — el servicio le quedaría inservible.
                RemindersRead,
                RemindersWrite,
                // Task: los tres operativos entran en el bundle del empleado. Assign también, y no
                // es una concesión: el flujo estrella del servicio es «preparar → revisión interna»,
                // donde el preparador le pasa la tarea al revisor. Sin tasks.assign por defecto ese
                // flujo no existe el día uno (§2.2 del modelo). Quedan fuera manage_all (override de
                // supervisión, por rol explícito) y templates.manage (configuración de la firma,
                // reservada a TenantAdmin — mismo criterio que ScribeTemplatesWrite).
                TasksRead,
                TasksWrite,
                TasksAssign,
                // Quien pide el documento es quien cierra lo que llega: separarlo obligaria a que
                // otra persona valide cada W-2, que no es como trabaja una firma.
                TasksClientRequestsManage,
                // El preparador agenda con sus clientes y bloquea su propia agenda. Fuera quedan
                // manage_all y types.manage: configuracion de la firma.
                CalendarRead,
                CalendarWrite,
                CalendarAvailabilityManage,
            ],
            Role.SystemCustomerPortal =>
            [
                PortalFoldersView,
                TasksPortalClientRequests,
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
    /// 2026-08-06 (hallazgo real, encontrado verificando self-healing de RolePermissionsProjections
    /// en Notes) — permisos con los que se siembra/reconcilia el rol de sistema TenantAdmin de CADA
    /// tenant (<see cref="RoleRepository.EnsureSystemRolesAsync"/> al crear el tenant,
    /// <c>SystemRolePermissionsSyncService</c> para reconciliar tenants existentes cuando el
    /// catálogo cambia). A diferencia de <see cref="SystemRoleDefaults"/>/<see cref="DefaultsFor"/>
    /// (que SÍ excluyen <see cref="Permission.IsDangerous"/> — correcto para el bundle sugerido al
    /// crear un rol CUSTOM vía <see cref="RolePermissionGuard"/>, donde un TenantAdmin no debe poder
    /// otorgar auto-escalada/billing/legal a un rol de staff sin decisión explícita), este método
    /// SÍ incluye <c>IsDangerous</c>: el rol de sistema TenantAdmin representa al dueño/admin raíz
    /// del propio tenant, y el propio catálogo documenta caso por caso que roles.manage/billing.*/
    /// subscription.manage/tenant_domains.manage/cloudstorage.legal.manage "SÍ tienen un caso de uso
    /// legítimo para un TenantAdmin" — la exclusión de IsDangerous nunca tuvo un mecanismo real de
    /// "asignación explícita" para llegar a ese rol de sistema, dejando a TODO tenant sin nadie
    /// capaz de gestionar roles/billing/dominios/legal-hold desde que existe el tenant. Sigue
    /// excluyendo PlatformOnly e IsCustomerPortal, igual que <see cref="SystemRoleDefaults"/>.
    /// </summary>
    public static IReadOnlyCollection<string> SystemTenantAdminRootPermissions() =>
        All.Where(definition => !definition.IsCustomerPortal && !definition.PlatformOnly)
            .Select(definition => definition.Code)
            .ToArray();

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
