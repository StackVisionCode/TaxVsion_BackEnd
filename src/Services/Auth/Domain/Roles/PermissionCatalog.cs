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
    public const string UsersManage = UserManagementPermissions.UsersManage;
    public const string RolesManage = "roles.manage";
    public const string AuditView = "audit.view";
    public const string SettingsManage = "settings.manage";

    // billing.* = billing de SUSCRIPCIÓN SaaS (métodos de pago, plan): peligroso/admin-only por diseño
    // (RBAC Fase 2). NO confundir con invoicing.* (facturación de clientes de la firma), que es trabajo
    // operativo diario del staff — permiso aparte, no peligroso, en el bundle del empleado.
    public const string BillingView = "billing.view";
    public const string BillingManage = "billing.manage";
    public const string SubscriptionManage = "subscription.manage";

    // Facturación tenant→cliente (servicio Billing: Invoices + IssuerProfile). Operativo, no peligroso.
    public const string InvoicingView = "invoicing.view";
    public const string InvoicingManage = "invoicing.manage";
    public const string InvoicingIssuerManage = "invoicing.issuer.manage";
    public const string TenantDomainsManage = "tenant.domains.manage";
    public const string BrandingManage = TenantBrandingPermissions.Manage;
    public const string PlatformBrandingManage = TenantBrandingPermissions.Platform;

    // Módulos operativos
    public const string CustomersView = CustomersPermissions.View;
    public const string CustomersViewAll = CustomersPermissions.ViewAll;
    public const string CustomersManage = CustomersPermissions.Manage;
    public const string CustomersFiscalProfileReveal = CustomersPermissions.FiscalProfileReveal;
    public const string CustomersPreparerManage = CustomersPermissions.PreparerManage;
    public const string CustomersImport = CustomersPermissions.Import;
    public const string SignaturesRequest = "signatures.request";
    public const string DocumentsView = "documents.view";
    public const string DocumentsManage = "documents.manage";
    public const string DocumentsBrandingManage = DocumentsPermissions.BrandingManage;
    public const string EmailUse = "email.use";
    public const string CommsCalls = "comms.calls";
    public const string CampaignsView = CampaignsPermissions.View;
    public const string CampaignsManage = CampaignsPermissions.Manage;
    public const string CampaignsSend = CampaignsPermissions.Send;
    public const string CampaignsSendersManage = CampaignsPermissions.SendersManage;
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
    public const string CloudStorageDmcaManage = CloudStoragePermissions.DmcaManage;
    public const string CloudStorageDmcaCounterNotice = CloudStoragePermissions.DmcaCounterNotice;

    // Signature — firma electrónica (bounded context propio, ver microservicio Signature)
    public const string SignatureRequestCreate = SignaturePermissions.RequestCreate;
    public const string SignatureRequestRead = SignaturePermissions.RequestRead;
    public const string SignatureRequestCancel = SignaturePermissions.RequestCancel;
    public const string SignatureRequestResend = SignaturePermissions.RequestResend;
    public const string SignatureRequestExpire = SignaturePermissions.RequestExpire;
    public const string SignatureRequestManage = SignaturePermissions.RequestManage;
    public const string SignatureLegalManage = SignaturePermissions.LegalHoldManage;
    public const string SignatureDocumentPrepare = SignaturePermissions.DocumentPrepare;
    public const string SignatureDocumentSign = SignaturePermissions.DocumentSign;
    public const string SignatureDocumentView = SignaturePermissions.DocumentView;
    public const string SignatureDocumentDownload = SignaturePermissions.DocumentDownload;
    public const string SignatureDocumentSend = SignaturePermissions.DocumentSend;
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
    public const string CorrespondenceManage = CorrespondencePermissions.Manage;

    // Connectors — cuentas de correo conectadas (OAuth Gmail/Graph o IMAP+SMTP manual) que
    // alimentan el envío/recepción de Correspondence (bounded context propio, ver microservicio
    // Connectors). Fase 6.5 (hardening): estos dos permisos ya los exigían los controllers de
    // Connectors vía [HasPermission(...)] desde que se construyeron, pero nunca se habían
    // sembrado en este catálogo — sin fila real, ningún rol podía tenerlos asignados.
    public const string ConnectorsAccountsRead = ConnectorsPermissions.AccountsRead;
    public const string ConnectorsAccountsWrite = ConnectorsPermissions.AccountsWrite;
    public const string ConnectorsAccountsConnectOwn = ConnectorsPermissions.AccountsConnectOwn;
    public const string ConnectorsAccountsOfficeRead = ConnectorsPermissions.AccountsOfficeRead;

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

    // Lectura del historial/opt-outs (endpoints GET del CRM) y gestión manual de bajas.
    public const string SmsRead = SmsPermissions.Read;
    public const string SmsManage = SmsPermissions.Manage;

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
    // vía [HasPermission(...)], pero nunca se habían sembrado en este catálogo. LogView lo exige
    // ahora el GET de NotificationsController (historial del tenant para auditoría/soporte).
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
    public const string PortalCallsUse = PortalPermissions.CallsUse;
    public const string PortalMilesUse = PortalPermissions.MilesUse;
    public const string PortalFoldersView = PortalPermissions.FoldersView;

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
        bool IsDangerous = false,
        // Declarado pero sin ningún endpoint que lo exija todavía: no se concede a nadie ni se
        // ofrece en el cajón de accesos. Ver Permission.IsReserved.
        bool IsReserved = false
    );

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(new Guid("a1000000-0000-0000-0000-000000000001"), UsersView, "users", "View users", false),
        new(new Guid("a1000000-0000-0000-0000-000000000002"), UsersInvite, "users", "Invite users", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000003"),
            UsersManage,
            "users",
            "Activate, deactivate and edit users",
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
            "Manage roles and permissions",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            IsAssignableByTenant: false,
            IsDangerous: true
        ),
        new(new Guid("a1000000-0000-0000-0000-000000000005"), AuditView, "audit", "View the audit log", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000006"),
            SettingsManage,
            "settings",
            "Manage office settings",
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
            "View billing and subscription",
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
            "Manage payment methods and billing",
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
            "Change plan and manage the subscription",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            IsAssignableByTenant: false,
            IsDangerous: true
        ),
        // Facturación tenant→cliente (servicio Billing). A DIFERENCIA de billing.* (suscripción SaaS,
        // peligroso), esto es trabajo operativo diario: emitir/leer facturas y configurar los datos del
        // emisor. No peligroso, asignable, desde Starter — llega al TenantAdmin por el bundle automático
        // y al empleado por su array explícito. AllowedActorTypes=null infiere staff (no portal).
        new(new Guid("a1000000-0000-0000-0000-000000000180"), InvoicingView, "billing", "View client invoices", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000181"),
            InvoicingManage,
            "billing",
            "Create, issue and manage client invoices",
            false
        ),
        new(new Guid("a1000000-0000-0000-0000-000000000010"), CustomersView, "customers", "View clients", false),
        new(
            new Guid("a1000000-0000-0000-0000-0000000000c9"),
            CustomersViewAll,
            "customers",
            "View ALL clients, not only the assigned ones",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000011"),
            CustomersManage,
            "customers",
            "Create and edit clients",
            false
        ),
        new(
            // Importación masiva. NO viene en el bundle de SystemEmployee: por defecto solo el
            // administrador lo tiene. Pero es concedible a un empleado con un rol custom, y desde
            // entonces importa de verdad — `CustomerImportsController` admite TenantEmployee y deja
            // la decisión en este permiso. No confundir con CustomersManage, que el empleado sí trae.
            new Guid("a1000000-0000-0000-0000-00000000009c"),
            CustomersImport,
            "customers",
            "Bulk import clients (CSV/Excel)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000012"),
            // LEGACY. Lo reemplazó el set granular signature.request.* (create/read/cancel/resend/
            // expire): este nunca gateó nada. No se borra porque la fila ya está sembrada en
            // producción — se reserva, que es lo que lo saca del cajón de accesos.
            SignaturesRequest,
            "signatures",
            "Request signatures",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        // El microservicio Documents no expone lectura de documentos: solo generación (M2M) y
        // branding. Los archivos del cliente son CloudStorage, con cloudstorage.file.*. Reservado hasta
        // que Documents tenga su propia superficie de usuario.
        new(
            new Guid("a1000000-0000-0000-0000-000000000013"),
            DocumentsView,
            "documents",
            "View documents",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000014"),
            // Idem view: fuera del branding —que ya tiene documents.branding.manage— no hay nada
            // que gestionar todavía en Documents. Reservado.
            DocumentsManage,
            "documents",
            "Manage documents",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000152"),
            DocumentsBrandingManage,
            "documents",
            "Configure document branding",
            false
        ),
        new(
            // Módulo "email" solo disponible desde el plan Pro (ver SubscriptionPlanCatalogSeeder).
            new Guid("a1000000-0000-0000-0000-000000000015"),
            // Redundante: QUIEN puede usar el correo lo deciden los correspondence.* (leer, redactar,
            // responder, enviar) y SI el plan lo incluye lo decide el gate del módulo "email" (Pro+).
            EmailUse,
            "email",
            "Use the email module",
            false,
            MinPlanTier: (int)PlanTier.Pro,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            // MinPlanTier Pro es inerte acá: reservado + no asignable por el tenant = no gatea nada.
            // (El módulo "comms" YA no es solo de Pro — chat, llamadas y vídeo van en todos los planes
            // desde que se separó `meetings`; este permiso legacy se deja tal cual, no se toca.)
            new Guid("a1000000-0000-0000-0000-000000000016"),
            // LEGACY. Lo reemplazaron communication.call.start, videocall.start y meeting.*: este
            // nunca gateó nada. Mismo criterio que signatures.request — reservado, no borrado.
            CommsCalls,
            "comms",
            "Make calls and hold meetings",
            false,
            MinPlanTier: (int)PlanTier.Pro,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            // Módulo "campaigns" solo disponible desde el plan Pro. Los cuatro permisos del servicio
            // separan ver, editar, enviar y administrar remitentes: antes uno solo cubría todo, así
            // que delegar "que preparen la campaña" delegaba también "que la manden a la cartera".
            new Guid("a1000000-0000-0000-0000-000000000017"),
            CampaignsManage,
            "campaigns",
            "Create and edit campaigns, contacts and lists",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            // El emisor legal (razón social, RNC/EIN, dirección fiscal) sale de invoicing.manage:
            // emitir una factura es trabajo diario del preparador, cambiar con qué identidad fiscal
            // factura la oficina no lo es. Queda administrativo — fuera del bundle del empleado.
            new Guid("a1000000-0000-0000-0000-000000000186"),
            InvoicingIssuerManage,
            "billing",
            "Edit the legal issuer on the office's invoices",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000183"),
            CampaignsView,
            "campaigns",
            "View campaigns, contacts, lists, schedules and runs",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            // Disparar o programar un envío masivo es la acción irreversible del servicio: sale de
            // campaigns.manage para poder delegar la preparación sin delegar el envío.
            new Guid("a1000000-0000-0000-0000-000000000184"),
            CampaignsSend,
            "campaigns",
            "Send or schedule a campaign",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            // De quién sale el correo de la oficina es identidad, no contenido: queda administrativo
            // (fuera del bundle del empleado), igual que postmaster.providers.write.
            new Guid("a1000000-0000-0000-0000-000000000185"),
            CampaignsSendersManage,
            "campaigns",
            "Manage campaign sender profiles",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            // Módulo "reports" solo disponible desde el plan Pro.
            new Guid("a1000000-0000-0000-0000-000000000018"),
            // Todavía no hay microservicio de reportes. El código queda declarado para cuando exista,
            // pero reservado: hoy no protege nada y no debe ofrecerse como si lo hiciera.
            ReportsView,
            "reports",
            "View the dashboard and reports",
            false,
            MinPlanTier: (int)PlanTier.Pro,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            // Lo exigen las rutas de llamada de Communication solo para el actor CustomerPortal
            // (equivalente en Node de HasPermissionForActor): es la palanca que el administrador
            // reconoce en el cajón de accesos del cliente para quitarle las llamadas.
            new Guid("a1000000-0000-0000-0000-000000000019"),
            PortalCallsUse,
            "portal",
            "The client can make calls",
            true
        ),
        new(
            // Reservado: no existe módulo "miles" ni ningún endpoint que lo exija. Se deja declarado
            // (la fila ya está sembrada en producción) pero sin concederse a nadie hasta que la
            // función exista — un permiso que no protege nada solo ensucia el cajón de accesos.
            new Guid("a1000000-0000-0000-0000-000000000020"),
            PortalMilesUse,
            "portal",
            "The client can use mileage tracking",
            true,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000021"),
            PortalFoldersView,
            "portal",
            "The client can see the folders on their profile",
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
            "View file details",
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
            "Upload files through the secure gateway",
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
            "Download available files",
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
            "Delete files",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000027"),
            CloudStorageSettingsManage,
            "cloudstorage",
            "Manage storage policies",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000028"),
            CloudStorageAuditView,
            "cloudstorage",
            "View the file audit log",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000065"),
            CloudStorageRecycleBinManage,
            "cloudstorage",
            "Restore and purge files from the recycle bin",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000066"),
            CloudStorageFolderManage,
            "cloudstorage",
            "Create, rename and move folders",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000067"),
            CloudStorageShareCreate,
            "cloudstorage",
            "Create share links for files",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000068"),
            CloudStorageShareRevoke,
            "cloudstorage",
            "Revoke existing share links",
            false
        ),
        new(
            // Reservado: habilita otorgar permisos de Upload/EditMetadata en un
            // link publico y cambiar la expiracion de cualquier link del tenant —
            // ambos con impacto directo en la exposicion de datos fiscales.
            new Guid("a1000000-0000-0000-0000-000000000069"),
            CloudStorageShareManage,
            "cloudstorage",
            "Grant elevated access on share links and manage their expiry",
            false,
            IsAssignableByTenant: false
        ),
        new(
            // Legal hold sobre archivos del PROPIO tenant: el admin raíz sí tiene un caso de uso
            // legítimo (retener evidencia de un litigio propio), así que sigue siendo del tenant.
            // IsDangerous + no delegable: nunca llega a un rol de staff sin decisión explícita.
            // El DMCA, que antes compartía este permiso, se separó en CloudStorageDmcaManage.
            new Guid("a1000000-0000-0000-0000-000000000070"),
            CloudStorageLegalManage,
            "cloudstorage",
            "Place and lift legal holds on this office's files",
            false,
            IsAssignableByTenant: false,
            IsDangerous: true
        ),
        new(
            // Solo plataforma: registrar un takedown DMCA y cerrarlo reinstalando el archivo es
            // del equipo legal de TaxVision, que responde ante el reclamante. Un tenant nunca
            // recibe la notificación ni tiene la obligación legal de tramitarla; lo que sí le
            // corresponde es la contranotificación (CloudStorageDmcaCounterNotice).
            new Guid("a1000000-0000-0000-0000-000000000182"),
            CloudStorageDmcaManage,
            "cloudstorage",
            "Record and close DMCA takedowns for any office",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
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
            "File a DMCA counter-notice for one of this office's files",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000072"),
            CorrespondenceRead,
            "correspondence",
            "View the client correspondence inbox",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000073"),
            CorrespondenceAttachmentDownload,
            "correspondence",
            "Download attachments from the correspondence inbox",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000074"),
            CorrespondenceCompose,
            "correspondence",
            "Create, edit and discard correspondence drafts",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000075"),
            CorrespondenceReply,
            "correspondence",
            "Reply to an incoming correspondence message",
            false
        ),
        new(
            // Enviar es una acción irreversible (llama a Postmaster, un correo real sale por el
            // proveedor conectado) — riesgo distinto de Compose/Reply, mismo criterio que separó
            // esos dos entre sí (plan §27, Fase 14).
            new Guid("a1000000-0000-0000-0000-000000000076"),
            CorrespondenceSend,
            "correspondence",
            "Send a correspondence draft that is ready to go",
            false
        ),
        new(
            // Gestión de bandeja (archivar/papelera/restaurar/purgar). Incluye la purga permanente, por
            // eso no va bajo CorrespondenceRead — mismo criterio admin-only que CloudStorageShareManage:
            // no se otorga al empleado por defecto (no está en SystemRoleDefaults(SystemEmployee)).
            new Guid("a1000000-0000-0000-0000-00000000009a"),
            CorrespondenceManage,
            "correspondence",
            "Archive, trash, restore and permanently delete correspondence",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000077"),
            ConnectorsAccountsRead,
            "connectors",
            "View the office's connected email accounts",
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
            "Connect, reconnect and disconnect the office's email accounts",
            false
        ),
        new(
            // Conectar/administrar SOLO el buzón personal propio (mismo riesgo que ConnectorsAccountsWrite
            // pero acotado al correo del propio empleado): assignable por el tenant para que el
            // TenantAdmin lo delegue sin dar el write completo (que administra el buzón de oficina y
            // cualquiera). No se otorga por defecto al empleado.
            new Guid("a1000000-0000-0000-0000-0000000000c7"),
            ConnectorsAccountsConnectOwn,
            "connectors",
            "Connect and manage your own personal mailbox",
            false
        ),
        new(
            // Ver el buzón de oficina y su correo. ON por defecto en el empleado; deny per-usuario.
            new Guid("a1000000-0000-0000-0000-0000000000c8"),
            ConnectorsAccountsOfficeRead,
            "connectors",
            "View the office mailbox and its mail",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000079"),
            ScribeTemplatesRead,
            "scribe",
            "View email templates (system and this office's)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000080"),
            ScribeTemplatesWrite,
            "scribe",
            "Create, edit and publish email template versions",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000081"),
            // EmailLayoutsController solo expone escrituras (crear, versionar, publicar); no hay GET
            // de layouts que gatear. Reservado hasta que exista.
            ScribeLayoutsRead,
            "scribe",
            "View email layouts (system and this office's)",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000082"),
            ScribeLayoutsWrite,
            "scribe",
            "Create, edit and publish email layout versions",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000083"),
            ScribeEventMappingsRead,
            "scribe",
            "View the rules that resolve an event to a template",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000084"),
            ScribeEventMappingsWrite,
            "scribe",
            "Create, edit and delete the rules that resolve an event to a template",
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
            // Scribe no tiene superficie de campañas: son de su propio servicio, con campaigns.*.
            // Reservado.
            ScribeCampaignsRead,
            "scribe",
            "View email campaigns built on templates (reserved, no endpoint yet)",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000086"),
            // Idem read: las campañas no viven en Scribe. Reservado.
            ScribeCampaignsWrite,
            "scribe",
            "Manage email campaigns built on templates (reserved, no endpoint yet)",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
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
            "Render templates (service-to-service)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        // SMS — a diferencia de ScribeRender, sí es un permiso humano-asignable: un TenantAdmin lo
        // recibe vía SystemRoleDefaults (no CustomerPortal, no PlatformOnly, no Dangerous), y el
        // caller M2M lo lleva como claim "perm" vía ServiceAuth:Clients (config, no rol). El
        // endpoint "POST /sms/messages" toma el TenantId del TOKEN (no del body), así que no aplica
        // el riesgo cross-tenant que obligó a marcar ScribeRender como PlatformOnly.
        new(new Guid("a1000000-0000-0000-0000-000000000158"), SmsSend, "sms", "Send SMS and MMS messages", false),
        // Lectura del historial de SMS y opt-outs desde el CRM (endpoints GET). Humano-asignable
        // (TenantAdmin/TenantEmployee vía defaults). Mismo módulo "sms" (para el gate de addon, F2).
        new(
            new Guid("a1000000-0000-0000-0000-0000000001F0"),
            SmsRead,
            "sms",
            "View the SMS history, its status and opt-outs",
            false
        ),
        // Gestión manual del consentimiento (baja/alta de un teléfono). Administración del tenant.
        new(
            new Guid("a1000000-0000-0000-0000-0000000001F1"),
            SmsManage,
            "sms",
            "Manually manage SMS opt-outs and opt-ins",
            false
        ),
        // Catalog — productos/servicios/categorías. Humano-asignables (TenantAdmin vía defaults).
        new(
            new Guid("a1000000-0000-0000-0000-000000000159"),
            CatalogRead,
            "catalog",
            "View the product and service catalog",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000160"),
            CatalogWrite,
            "catalog",
            "Create and edit products, services and categories",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000161"),
            CatalogDelete,
            "catalog",
            "Delete products, services and categories",
            false
        ),
        // Inventory
        new(
            new Guid("a1000000-0000-0000-0000-000000000162"),
            InventoryRead,
            "inventory",
            "View stock, suppliers and movements",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000163"),
            InventoryWrite,
            "inventory",
            "Manage suppliers and stock thresholds",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000164"),
            InventoryAdjust,
            "inventory",
            "Adjust stock by recording movements",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000029"),
            SignatureRequestCreate,
            "signature",
            "Create e-signature requests",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000030"),
            SignatureRequestRead,
            "signature",
            "View signature requests",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000031"),
            SignatureRequestCancel,
            "signature",
            "Cancel signature requests",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000032"),
            SignatureRequestResend,
            "signature",
            "Resend invitations to signers",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000033"),
            SignatureRequestExpire,
            "signature",
            "Extend the expiry of signature requests",
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
            "Manage signature requests created by other people in the office",
            false,
            IsAssignableByTenant: false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000034"),
            SignatureDocumentPrepare,
            "signature",
            "Validate and prepare documents for signing",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000035"),
            SignatureDocumentSign,
            "signature",
            "Apply the preparer's signature to a document",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000036"),
            // No hay endpoint: ver un documento firmado es GET signature/requests/{id}, que ya
            // exige request.read. Reservado hasta que exista una vista de documentos propia.
            SignatureDocumentView,
            "signature",
            "View signed documents and their details",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000037"),
            // No hay endpoint: el sellado vive en CloudStorage y se descarga con
            // cloudstorage.file.download. Reservado para no prometer un control que no existe.
            SignatureDocumentDownload,
            "signature",
            "Download the sealed file, the original or the certificate",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            // Controla la ENTREGA hacia afuera (email/SMS del documento firmado y del certificado al
            // firmante). Separado de crear/firmar para que el preparador lo niegue por-empleado con el
            // deny-layer. Asignable por el tenant; no peligroso, no platform-only.
            new Guid("a1000000-0000-0000-0000-0000000000a0"),
            SignatureDocumentSend,
            "signature",
            "Email or text the signed document and certificate to the signers",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000038"),
            // No hay endpoint de staff: el audit trail solo se consulta desde el enlace
            // publico del firmante, que es anonimo por token. Reservado.
            SignatureDocumentAuditRead,
            "signature",
            "View a signature's audit trail",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            // Legal hold escribe estado (bloquea el borrado), no es una lectura del audit trail — por
            // eso se separa de DocumentAuditRead. Admin-only por defecto (no está en la lista Employee).
            new Guid("a1000000-0000-0000-0000-00000000009b"),
            SignatureLegalManage,
            "signature",
            "Place and lift legal holds on a signature",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000039"),
            SignatureTemplateCreate,
            "signature",
            "Create reusable signature templates",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000040"),
            SignatureTemplateUpdate,
            "signature",
            "Edit signature templates",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000041"),
            SignatureTemplateDelete,
            "signature",
            "Delete signature templates",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000042"),
            SignatureSettingsManage,
            "signature",
            "Manage the office's signature settings",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000043"),
            SignaturePreparerManage,
            "signature",
            "Manage the preparer's saved signatures",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000044"),
            // Es un endpoint PÚBLICO (anónimo): no hay usuario a quien exigirle un permiso,
            // así que este código nunca podrá aplicarse tal como esta. Reservado.
            SignatureCertificateVerify,
            "signature",
            "Verify signature certificates (public endpoint)",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
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
            "Manage an office's Signature plan limits (platform only)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true,
            IsDangerous: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000063"),
            CustomersFiscalProfileReveal,
            "customers",
            "Reveal a client's full SSN/ITIN/EIN",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000141"),
            CustomersPreparerManage,
            "customers",
            "Assign or reassign a client's preparer",
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
            "Manage the office's own domains (custom hostnames)",
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
            "Start chat conversations",
            true,
            MinPlanTier: (int)PlanTier.Starter,
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
            "Reply in chat conversations",
            true,
            MinPlanTier: (int)PlanTier.Starter,
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
            "Moderate messages in the office's conversations",
            false,
            MinPlanTier: (int)PlanTier.Starter
        ),
        new(
            // MinPlanTier Starter (no Pro) desde que `comms` entró en Starter: el techo de plan
            // (§27) decide qué puede OTORGAR un tenant a un rol propio, así que dejarlo en Pro
            // habría dado un Starter con chat incluido en el plan pero sin poder delegarlo. Los
            // cuatro de `communication.meeting.*` sí se quedan en Pro — las reuniones se venden
            // aparte (módulo `meetings`).
            // Explícito (no inferido): a diferencia de ChatStart/ChatReply arriba, este tiene
            // IsCustomerPortal=false (infiere staff-only), pero SystemRoleDefaults
            // (SystemCustomerPortal) también lo otorga — el cliente abre su propio chat de
            // soporte hacia el PlatformTenant. Mismo bug, sentido inverso (mismo hallazgo de
            // Fase 7 que ChatStart/ChatReply arriba).
            new Guid("a1000000-0000-0000-0000-000000000048"),
            CommunicationSupportOpen,
            "communication",
            "Open a support chat with the platform",
            false,
            MinPlanTier: (int)PlanTier.Starter,
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
            "Handle support chats as an agent (platform only)",
            false,
            MinPlanTier: (int)PlanTier.Starter
        ),
        new(
            // Dual-use (staff Y cliente): el cliente ahora puede LLAMAR a su preparador desde el
            // chat del portal, no solo recibir. IsCustomerPortal=false (staff-primario) pero se
            // lista CustomerPortal explícito para que el ActorTypeRoleGuard permita otorgarlo al rol
            // SystemCustomerPortal — mismo patrón que SupportOpen/ChatStart arriba.
            new Guid("a1000000-0000-0000-0000-000000000050"),
            CommunicationCallStart,
            "communication",
            "Start one-to-one audio calls",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            AllowedActorTypes:
            [
                UserActorType.TenantEmployee,
                UserActorType.TenantAdmin,
                UserActorType.PlatformAdmin,
                UserActorType.CustomerPortal,
            ]
        ),
        new(
            // Ver nota de CommunicationCallStart — mismo caso (el cliente inicia videollamada al preparador).
            new Guid("a1000000-0000-0000-0000-000000000051"),
            CommunicationVideoCallStart,
            "communication",
            "Start one-to-one video calls",
            false,
            MinPlanTier: (int)PlanTier.Starter,
            AllowedActorTypes:
            [
                UserActorType.TenantEmployee,
                UserActorType.TenantAdmin,
                UserActorType.PlatformAdmin,
                UserActorType.CustomerPortal,
            ]
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000052"),
            CommunicationCallRecord,
            "communication",
            "Record one-to-one calls (with a disclosure banner)",
            false,
            MinPlanTier: (int)PlanTier.Starter
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000053"),
            CommunicationMeetingCreate,
            "communication",
            "Create multi-party meetings",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            // Ver nota de CommunicationChatStart — mismo caso (staff Y cliente lo tienen hoy).
            new Guid("a1000000-0000-0000-0000-000000000054"),
            CommunicationMeetingJoin,
            "communication",
            "Join meetings with a valid invitation",
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
            "Host meetings (waiting room, mute all, transfer)",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000056"),
            CommunicationMeetingRecord,
            "communication",
            "Record meetings (with a disclosure banner)",
            false,
            MinPlanTier: (int)PlanTier.Pro
        ),
        new(
            // Ver nota de CommunicationChatStart — mismo caso (staff Y cliente lo tienen hoy).
            new Guid("a1000000-0000-0000-0000-000000000057"),
            CommunicationScreenshotCreate,
            "communication",
            "Attach screenshots, voice notes and video in chat",
            true,
            MinPlanTier: (int)PlanTier.Starter,
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
            "Create internal groups",
            false,
            MinPlanTier: (int)PlanTier.Starter
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000059"),
            CommunicationGroupManageMembers,
            "communication",
            "Manage internal group members",
            false,
            MinPlanTier: (int)PlanTier.Starter
        ),
        new(
            // Ver nota de CommunicationChatStart — mismo caso (staff Y cliente lo tienen hoy).
            new Guid("a1000000-0000-0000-0000-000000000060"),
            CommunicationNotificationRead,
            "communication",
            "View your own in-app notifications",
            true,
            MinPlanTier: (int)PlanTier.Starter,
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
            "Manage the office's communication settings",
            false,
            MinPlanTier: (int)PlanTier.Starter
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000062"),
            CommunicationAnalyticsRead,
            "communication",
            "View the office's communication analytics",
            false,
            MinPlanTier: (int)PlanTier.Starter
        ),
        // Postmaster (ver comentario junto a los const de arriba).
        new(
            new Guid("a1000000-0000-0000-0000-000000000089"),
            PostmasterMessagesRead,
            "postmaster",
            "View the office's sent email history",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000090"),
            PostmasterSuppressionRead,
            "postmaster",
            "View the suppression list (addresses that bounced or unsubscribed)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000091"),
            PostmasterSuppressionWrite,
            "postmaster",
            "Add or remove addresses from the suppression list",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000092"),
            PostmasterProvidersRead,
            "postmaster",
            "View the office's configured email provider",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000093"),
            PostmasterProvidersWrite,
            "postmaster",
            "Configure the office's email provider (SMTP/API)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000095"),
            NotificationEmailSend,
            "notification",
            "Send a one-off email",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000096"),
            NotificationEmailView,
            "notification",
            "View the sent email history",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000097"),
            NotificationTemplateView,
            "notification",
            "View the office's email templates",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000098"),
            NotificationTemplateManage,
            "notification",
            "Create, edit and publish the office's email templates",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000099"),
            NotificationLayoutManage,
            "notification",
            "Manage the office's base email layouts",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000100"),
            // El motor de EmailCampaigns de Notification fue RETIRADO (ver EmailSendController): estos
            // dos quedaron sin superficie. Las campañas son hoy el servicio Campaigns, con campaigns.*.
            NotificationCampaignView,
            "notification",
            "View the office's email campaigns (reserved, no endpoint yet)",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000101"),
            // Idem view: resto del motor retirado. Reservado.
            NotificationCampaignManage,
            "notification",
            "Manage the office's email campaigns (reserved, no endpoint yet)",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000102"),
            NotificationLogView,
            "notification",
            "View the notification history (email, SMS and in-app) for audit and support",
            false
        ),
        // PaymentApp (ver comentario junto a los const de arriba).
        new(
            new Guid("a1000000-0000-0000-0000-000000000103"),
            PaymentAppSaaSPaymentRead,
            "payment_app",
            "View the office's own SaaS payments (subscription, seats and add-ons)",
            false
        ),
        new(
            // Solo plataforma: un tenant nunca se reembolsa su propia suscripción.
            new Guid("a1000000-0000-0000-0000-000000000104"),
            PaymentAppSaaSPaymentRefund,
            "payment_app",
            "Refund a SaaS payment for any office (platform support)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000105"),
            PaymentAppProviderCustomerRead,
            "payment_app",
            "View the office's saved payment method",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000106"),
            PaymentAppProviderCustomerManage,
            "payment_app",
            "Manage the office's saved payment method",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000107"),
            PaymentAppAdminCrossTenant,
            "payment_app",
            "View SaaS payments for ANY office, including suspended ones (platform only)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        // PaymentClient (ver comentario junto a los const de arriba).
        new(
            new Guid("a1000000-0000-0000-0000-000000000108"),
            PaymentClientConfigRead,
            "payment_client",
            "View the office's payment processing setup",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000109"),
            PaymentClientConfigManage,
            "payment_client",
            "Configure the office's payment processing mode and credentials",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000110"),
            PaymentClientPaymentRead,
            "payment_client",
            "View the payments the office collected from its clients",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000111"),
            PaymentClientPaymentCharge,
            "payment_client",
            "Charge a payment to one of the office's clients",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000112"),
            // TenantPaymentsController solo cobra y consulta; no hay endpoint de reembolso. Reservado
            // hasta que exista — y ojo: cuando exista, el plan lo marca IsDangerous.
            PaymentClientPaymentRefund,
            "payment_client",
            "Refund a payment collected from one of the office's clients",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000113"),
            PaymentClientPaymentLinkRead,
            "payment_client",
            "View the office's payment links",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000114"),
            PaymentClientPaymentLinkManage,
            "payment_client",
            "Create and manage the office's payment links",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000115"),
            PaymentClientConnectAccountRead,
            "payment_client",
            "View the status of the office's Stripe Connect account",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000116"),
            PaymentClientConnectAccountOnboard,
            "payment_client",
            "Start onboarding for the office's Stripe Connect account",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000117"),
            PaymentClientPayoutRead,
            "payment_client",
            "View the office's scheduled payouts",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000118"),
            PaymentClientPayoutManage,
            "payment_client",
            "Manage the office's payout schedule",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000119"),
            PaymentClientRecurringRead,
            "payment_client",
            "View the office's recurring payments",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000120"),
            PaymentClientRecurringManage,
            "payment_client",
            "Create and manage the office's recurring payments",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000121"),
            PaymentClientAdminCrossTenant,
            "payment_client",
            "View payments for ANY office, including suspended ones (platform only)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000122"),
            BrandingManage,
            "branding",
            "Manage the office's logo and branding",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000123"),
            GrowthCodesRead,
            "codes",
            "View the office's codes",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000124"),
            GrowthCodesManage,
            "codes",
            "Manage the office's codes",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000125"),
            // Growth declara el catálogo completo del modulo, pero de referidos y códigos solo están
            // construidos 6 endpoints humanos (crear/ver/activar/revocar un código, crear una
            // atribución y emitir el código propio). Este no tiene endpoint: reservado hasta que
            // exista, para no ofrecer en el cajón de accesos un control que no existe.
            GrowthCodesIssue,
            "codes",
            "Issue benefit codes",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(new Guid("a1000000-0000-0000-0000-000000000126"), GrowthCodesActivate, "codes", "Activate codes", false),
        new(new Guid("a1000000-0000-0000-0000-000000000127"), GrowthCodesRevoke, "codes", "Revoke codes", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000128"),
            // Growth declara el catálogo completo del modulo, pero de referidos y códigos solo están
            // construidos 6 endpoints humanos (crear/ver/activar/revocar un código, crear una
            // atribución y emitir el código propio). Este no tiene endpoint: reservado hasta que
            // exista, para no ofrecer en el cajón de accesos un control que no existe.
            GrowthCodesAuditRead,
            "codes",
            "View the code audit log",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000129"),
            // Growth declara el catálogo completo del modulo, pero de referidos y códigos solo están
            // construidos 6 endpoints humanos (crear/ver/activar/revocar un código, crear una
            // atribución y emitir el código propio). Este no tiene endpoint: reservado hasta que
            // exista, para no ofrecer en el cajón de accesos un control que no existe.
            GrowthCodesRedemptionRead,
            "codes",
            "View code redemptions",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000130"),
            // Compensar una redención YA está protegido, pero por el otro mecanismo: el endpoint es M2M y usa
            // el scope de servicio growth.codes.compensate. No hay superficie humana. Reservado.
            GrowthCodesCompensationManage,
            "codes",
            "Manage promotional compensation",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000131"),
            GrowthReferralsOwnRead,
            "referrals",
            "View your own referrals",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000132"),
            // Growth declara el catálogo completo del modulo, pero de referidos y códigos solo están
            // construidos 6 endpoints humanos (crear/ver/activar/revocar un código, crear una
            // atribución y emitir el código propio). Este no tiene endpoint: reservado hasta que
            // exista, para no ofrecer en el cajón de accesos un control que no existe.
            GrowthReferralsProgramRead,
            "referrals",
            "View referral programs",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000133"),
            // Growth declara el catálogo completo del modulo, pero de referidos y códigos solo están
            // construidos 6 endpoints humanos (crear/ver/activar/revocar un código, crear una
            // atribución y emitir el código propio). Este no tiene endpoint: reservado hasta que
            // exista, para no ofrecer en el cajón de accesos un control que no existe.
            GrowthReferralsProgramManage,
            "referrals",
            "Manage referral programs",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000134"),
            // Growth declara el catálogo completo del modulo, pero de referidos y códigos solo están
            // construidos 6 endpoints humanos (crear/ver/activar/revocar un código, crear una
            // atribución y emitir el código propio). Este no tiene endpoint: reservado hasta que
            // exista, para no ofrecer en el cajón de accesos un control que no existe.
            GrowthReferralsAttributionRead,
            "referrals",
            "View referral attributions",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000135"),
            // Growth declara el catálogo completo del modulo, pero de referidos y códigos solo están
            // construidos 6 endpoints humanos (crear/ver/activar/revocar un código, crear una
            // atribución y emitir el código propio). Este no tiene endpoint: reservado hasta que
            // exista, para no ofrecer en el cajón de accesos un control que no existe.
            GrowthReferralsFraudRead,
            "referrals",
            "View anti-fraud reviews",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000136"),
            // Growth declara el catálogo completo del modulo, pero de referidos y códigos solo están
            // construidos 6 endpoints humanos (crear/ver/activar/revocar un código, crear una
            // atribución y emitir el código propio). Este no tiene endpoint: reservado hasta que
            // exista, para no ofrecer en el cajón de accesos un control que no existe.
            GrowthReferralsFraudManage,
            "referrals",
            "Manage anti-fraud reviews",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000137"),
            // Growth declara el catálogo completo del modulo, pero de referidos y códigos solo están
            // construidos 6 endpoints humanos (crear/ver/activar/revocar un código, crear una
            // atribución y emitir el código propio). Este no tiene endpoint: reservado hasta que
            // exista, para no ofrecer en el cajón de accesos un control que no existe.
            GrowthReferralsRewardRead,
            "referrals",
            "View rewards",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000138"),
            // Growth declara el catálogo completo del modulo, pero de referidos y códigos solo están
            // construidos 6 endpoints humanos (crear/ver/activar/revocar un código, crear una
            // atribución y emitir el código propio). Este no tiene endpoint: reservado hasta que
            // exista, para no ofrecer en el cajón de accesos un control que no existe.
            GrowthReferralsRewardManage,
            "referrals",
            "Manage non-monetary rewards",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000139"),
            // Growth declara el catálogo completo del modulo, pero de referidos y códigos solo están
            // construidos 6 endpoints humanos (crear/ver/activar/revocar un código, crear una
            // atribución y emitir el código propio). Este no tiene endpoint: reservado hasta que
            // exista, para no ofrecer en el cajón de accesos un control que no existe.
            GrowthReferralsAuditRead,
            "referrals",
            "View the referral audit log",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000140"),
            GrowthAdminCrossTenant,
            "growth",
            "Operate Growth resources for any office (platform only)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        // --- Subscription (RBAC Fase 8, ver comentario junto a los const de arriba) ---
        new(
            new Guid("a1000000-0000-0000-0000-000000000143"),
            SubscriptionPlanChange,
            "subscription",
            "Change plan, activate, cancel and manage the office's subscription",
            false,
            IsAssignableByTenant: false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000144"),
            SubscriptionSuspend,
            "subscription",
            "Suspend any office's subscription (platform only)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000145"),
            SubscriptionReactivate,
            "subscription",
            "Reactivate any office's subscription (platform only)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000146"),
            SubscriptionRenew,
            "subscription",
            "Manually renew any office's subscription (platform only)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000147"),
            SubscriptionAdminCrossTenant,
            "subscription",
            "View upcoming renewals, expired seats and past-due subscriptions for ANY office, and force an entitlements recalculation (platform only)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000148"),
            SeatsManage,
            "seats",
            "Buy, assign, release, reassign and renew the office's seats",
            false,
            IsAssignableByTenant: false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000149"),
            AddOnsManage,
            "addons",
            "Buy, cancel and renew the office's add-ons",
            false,
            IsAssignableByTenant: false
        ),
        // --- Tenant (RBAC Fase 8, ver comentario junto a los const de arriba) ---
        new(
            new Guid("a1000000-0000-0000-0000-000000000150"),
            TenantStatusChange,
            "tenant",
            "Change any office's status (platform only)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000151"),
            TenantListView,
            "tenant",
            "List every office on the platform (platform only)",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000153"),
            OnboardingAdminManage,
            "onboarding",
            "Review and resolve onboardings stuck in manual review or provisioning failure for any office",
            false,
            IsAssignableByTenant: false,
            PlatformOnly: true
        ),
        new(new Guid("a1000000-0000-0000-0000-000000000154"), NotesRead, "notes", "View notes", false),
        new(
            // ADR-06: Manage cubre crear/editar/pin/color/visibilidad/adjuntar — la regla "solo
            // el propio autor" NO vive acá (Permission no modela ownership), la aplica el handler
            // (note.CreatedByUserId == actorUserId) en Application, igual que Correspondence Draft.
            new Guid("a1000000-0000-0000-0000-000000000155"),
            NotesManage,
            "notes",
            "Create, edit, archive, restore and attach files to your own notes",
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
            "View, archive and delete notes from anyone in the office",
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
            "The client can read the notes marked as visible to them",
            true
        ),
        // Reminder — sin AllowedActorTypes explícito a propósito: la inferencia por defecto de
        // Permission da [TenantEmployee, TenantAdmin, PlatformAdmin], que es exactamente lo que
        // pide el diseño. Marcarlo a mano sería duplicar la regla y arriesgarse a que se desincronice.
        new(new Guid("a1000000-0000-0000-0000-000000000165"), RemindersRead, "reminders", "View your reminders", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000166"),
            RemindersWrite,
            "reminders",
            "Create, reschedule, snooze, dismiss and cancel your reminders",
            false
        ),
        // Task — los cinco sin AllowedActorTypes explícito, incluido ManageAll. La inferencia por
        // defecto da [TenantEmployee, TenantAdmin, PlatformAdmin] y eso es lo correcto acá, a
        // diferencia de NotesViewAll (que sí excluye a TenantEmployee): en una firma fiscal el
        // supervisor que revisa y desatasca es normalmente un preparador senior, no el admin del
        // tenant. Restringirlo a TenantAdmin dejaría al override sin poder otorgarse nunca a quien
        // de verdad lo ejerce. Lo que sí se hace es dejarlo FUERA del bundle por defecto del
        // empleado: se otorga por rol explícito.
        new(new Guid("a1000000-0000-0000-0000-000000000167"), TasksRead, "tasks", "View tasks", false),
        new(
            new Guid("a1000000-0000-0000-0000-000000000168"),
            TasksWrite,
            "tasks",
            "Create, edit, close and reopen your own or assigned tasks",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000169"),
            TasksAssign,
            "tasks",
            "Assign a task to someone else in the office",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000170"),
            TasksManageAll,
            "tasks",
            "Close, edit or reassign anyone's task (supervision)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000171"),
            TasksTemplatesManage,
            "tasks",
            "Create and edit the firm's task templates",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000172"),
            TasksClientRequestsManage,
            "tasks",
            "Ask clients for documents and close what they send",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000174"),
            CalendarRead,
            "calendar",
            "View the calendar and check availability",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000175"),
            CalendarWrite,
            "calendar",
            "Create, move and cancel your own appointments",
            false
        ),
        // No anula ADR-C-09: el agregado sigue exigiendo organizador. Permite actuar como tal.
        new(
            new Guid("a1000000-0000-0000-0000-000000000176"),
            CalendarManageAll,
            "calendar",
            "Reorganize other people's schedules as the organizer (supervision)",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000177"),
            CalendarTypesManage,
            "calendar",
            "Define the firm's appointment types",
            false
        ),
        new(
            new Guid("a1000000-0000-0000-0000-000000000178"),
            // AvailabilityController solo expone GET, y ya pide calendar.read. No hay escritura de
            // disponibilidad que gatear. Reservado.
            CalendarAvailabilityManage,
            "calendar",
            "Define working hours and calendar blocks",
            false,
            IsAssignableByTenant: false,
            IsReserved: true
        ),
        // El unico de este modulo cuyo destinatario esta fuera de la firma: el cliente ve su lista
        // de pedidos, no la tarea interna de la que salieron.
        new(
            new Guid("a1000000-0000-0000-0000-000000000173"),
            TasksPortalClientRequests,
            "tasks",
            "The client sees their requests and uploads what they're asked for",
            true
        ),
        // Marca del SISTEMA: solo PlatformAdmin. PlatformOnly excluye este permiso del bundle del
        // rol "Tenant Admin"; IsAssignableByTenant: false impide que un rol custom lo incluya.
        new(
            new Guid("a1000000-0000-0000-0000-000000000179"),
            PlatformBrandingManage,
            "branding",
            "Manage the platform's own brand (default colors, logo and favicon)",
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

    // Comms de doble uso: marcados IsCustomerPortal=true (staff Y cliente los usan), pero un
    // TenantAdmin/Owner real también los necesita. El filtro !IsCustomerPortal de los sets del
    // admin los excluía, dejando al Owner sin poder iniciar/responder chats, unirse a reuniones,
    // adjuntar archivos ni leer sus notificaciones. Se re-agregan explícitos, igual que ya se hace
    // para el rol Employee.
    private static readonly string[] TenantAdminCommunicationExtras =
    [
        CommunicationChatStart,
        CommunicationChatReply,
        CommunicationMeetingJoin,
        CommunicationScreenshotCreate,
        CommunicationNotificationRead,
    ];

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
            // A7: IsReserved también se excluye. Un reservado no gatea nada todavía; concederlo por
            // defecto lo haría aparecer como un acceso real que el administrador cree estar dando.
            Role.SystemTenantAdmin => All.Where(definition =>
                    !definition.IsCustomerPortal
                    && !definition.PlatformOnly
                    && !definition.IsDangerous
                    && !definition.IsReserved
                )
                .Select(definition => definition.Code)
                .Concat(TenantAdminCommunicationExtras)
                .Distinct()
                .ToArray(),
            Role.SystemEmployee =>
            [
                CustomersView,
                CustomersManage,
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
                // Signature: el empleado prepara solicitudes y consulta resultados. Cancelar entra:
                // quien la mandó es quien se da cuenta de que salió mal, y el ownership del handler
                // ya lo limita a las suyas. Fuera quedan settings, reservados a TenantAdmin.
                SignatureRequestCreate,
                SignatureRequestRead,
                SignatureRequestResend,
                // Extender el vencimiento ya lo venía haciendo el empleado: el endpoint pedía resend.
                // Al darle su permiso propio se explicita quién puede, sin quitárselo a nadie.
                SignatureRequestExpire,
                SignatureRequestCancel,
                // Su propia firma persistente. Antes bastaba con request.create, así que todo preparador
                // ya la administraba; sin esto, "My Signature" dejaría de funcionar para el empleado.
                SignaturePreparerManage,
                SignatureDocumentPrepare,
                SignatureDocumentSign,
                // view/download salen del bundle al quedar reservados: no gatean nada (ver documento es
                // request.read y el sellado se descarga con cloudstorage.file.download), y un reservado
                // no puede venir concedido de fábrica.
                SignatureDocumentSend,
                // Communication: mismo set que sembró la migración AddCommunicationPermissions
                // para el rol "Employee" — nunca host de settings/analytics/moderate/record.
                CommunicationChatStart,
                CommunicationChatReply,
                // Armar un grupo con dos colegas para coordinar un caso es trabajo diario; quedan
                // fuera moderate y group.manage_members, que son gobernanza del chat.
                CommunicationGroupCreate,
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
                // llegó) — no incluye accounts.write (conectar/desconectar el buzón de OFICINA
                // compartido es una acción de configuración reservada a TenantAdmin por defecto,
                // mismo criterio que CloudStorageSettingsManage/SignatureSettingsManage).
                ConnectorsAccountsRead,
                // connect_own: conectar/administrar su propio buzón personal. ON por defecto; el admin
                // lo restringe por usuario desde Edit access.
                ConnectorsAccountsConnectOwn,
                // office.read: ver el buzón de oficina y su correo. ON por defecto; deny per-usuario
                // para dejar al empleado solo con su personal.
                ConnectorsAccountsOfficeRead,
                // Scribe: el empleado puede ver los templates/layouts/event-mappings vigentes
                // (System y del tenant) para redactar/diagnosticar comunicaciones — mismo criterio
                // operativo que ConnectorsAccountsRead. No incluye templates.write/layouts.write/
                // event_mappings.write (crear o publicar una versión es un cambio de configuración
                // reservado a TenantAdmin por defecto, mismo criterio que ConnectorsAccountsWrite/
                // CloudStorageSettingsManage/SignatureSettingsManage), ni campaigns.read/write (sin
                // controller real todavía, ver PermissionDefinition), ni scribe.render (M2M-only,
                // nunca un permiso humano — ver PermissionDefinition).
                ScribeTemplatesRead,
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
                // Catalog (productos/servicios), Inventory y SMS son trabajo operativo diario de la firma
                // (facturar servicios, ajustar stock, avisar por SMS), no configuración administrativa —
                // mismo criterio que Reminders/Tasks/Calendar. Estaban en el catálogo (el TenantAdmin los
                // recibe por el filtro), pero nunca se agregaron a este bundle explícito, así que el
                // empleado recibía 403 en esas secciones. Son transversales (sin módulo): siempre efectivos.
                CatalogRead,
                CatalogWrite,
                CatalogDelete,
                InventoryRead,
                InventoryWrite,
                InventoryAdjust,
                SmsSend,
                SmsRead,
                // Facturación tenant→cliente (Invoices + IssuerProfile). Operativo diario del preparador,
                // no billing de suscripción (eso es billing.*, peligroso/admin-only, aparte a propósito).
                InvoicingView,
                InvoicingManage,
                // Campaigns: el preparador arma y manda las campañas de su firma. Fuera queda
                // senders.manage (la identidad del remitente de la oficina es configuración).
                // El módulo "campaigns" es de plan Pro: el gate de módulo lo filtra por plan.
                CampaignsView,
                CampaignsManage,
                CampaignsSend,
                // Notes: una nota es del propio autor (el handler lo impone con
                // CreatedByUserId == actorUserId), igual que un recordatorio. Sin estos dos el
                // empleado no podía ni escribirse una nota sobre el caso que está preparando — el
                // servicio le quedaba inservible. Fuera queda notes.view_all, que es gobernanza.
                NotesRead,
                NotesManage,
            ],
            Role.SystemCustomerPortal =>
            [
                PortalFoldersView,
                TasksPortalClientRequests,
                // El cliente lee las notas que su preparador marcó ClientVisible (PortalNotesController,
                // GET /notes/portal). Faltaba en el bundle del rol → el Portal recibía 403 aunque no
                // hubiera notas. `notes.manage`/`notes.read` son del staff; PortalRead es exclusivo del cliente.
                NotesPortalRead,
                CloudStorageFileView,
                CloudStorageFileUpload,
                CloudStorageFileDownload,
                // Communication: mismo set que sembró la migración AddCommunicationPermissions
                // para el rol "Customer Portal" — nunca moderate/host/record/settings.
                CommunicationChatStart,
                CommunicationChatReply,
                CommunicationSupportOpen,
                // El cliente puede INICIAR llamada/videollamada 1:1 a su preparador desde el chat del
                // portal (antes solo podía recibir). Gated por MinPlanTier=Pro como el staff.
                CommunicationCallStart,
                CommunicationVideoCallStart,
                // portal.calls.use es la palanca que el administrador ve en el cajón de accesos del
                // cliente ("quitarle las llamadas a este cliente"). Va al bundle ANTES de que las
                // rutas la exijan: sin esto, aplicarla dejaría sin llamadas a todos los clientes
                // que ya existen.
                PortalCallsUse,
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
        All.Where(definition => !definition.IsCustomerPortal && !definition.PlatformOnly && !definition.IsReserved)
            .Select(definition => definition.Code)
            .Concat(TenantAdminCommunicationExtras)
            .Distinct()
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
