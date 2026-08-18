namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition AuthLogin = Define(
        "auth.a.login",
        RateLimitCategory.A,
        RateLimitPartitionDimension.Email,
        [RateLimitPartitionDimension.Ip],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition AuthLoginByIp = Define(
        "auth.a.login_by_ip",
        RateLimitCategory.A,
        RateLimitPartitionDimension.Ip,
        [],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition AuthPasswordForgot = Define(
        "auth.b.password_forgot",
        RateLimitCategory.B,
        RateLimitPartitionDimension.Email,
        [RateLimitPartitionDimension.Ip],
        quota: 5,
        windowSeconds: 3600,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition AuthPasswordForgotByIp = Define(
        "auth.b.password_forgot_by_ip",
        RateLimitCategory.B,
        RateLimitPartitionDimension.Ip,
        [],
        quota: 20,
        windowSeconds: 3600,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition AuthOnboardingCheckoutCreate = Define(
        "auth.c.onboarding_checkout_create",
        RateLimitCategory.C,
        RateLimitPartitionDimension.Ip,
        [],
        quota: 5,
        windowSeconds: 3600,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition AuthOnboardingEmailChallengeCreate = Define(
        "auth.c.onboarding_email_challenge_create",
        RateLimitCategory.C,
        RateLimitPartitionDimension.Email,
        [],
        quota: 10,
        windowSeconds: 86_400,
        RateLimitAlgorithm.FixedWindow
    );

    // Auditoría post-Fase-9 (hallazgo #11) — AuditController.GetAuditLogs acepta filtros reales
    // (userId, action, rango de fechas) + paginación: búsqueda pesada (H), no lectura simple (F).
    // Era F desde Fase 4.12 por descuido de clasificación, nunca por falta de filtros.
    public static readonly RateLimitPolicyDefinition AuthAuditRead = Define(
        "auth.h.audit_read",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 100
    );

    // Compartida por MySessions + UserSessions (SessionsController).
    public static readonly RateLimitPolicyDefinition AuthSessionRead = Define(
        "auth.f.session_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition AuthTenantDomainRead = Define(
        "auth.f.tenant_domain_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition AuthTermsRead = Define(
        "auth.f.terms_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition AuthMfaRead = Define(
        "auth.f.mfa_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // MfaController.GetPolicy requiere SettingsManage (config de tenant), distinto de
    // auth.f.mfa_read (status del propio usuario) — cuota propia, mismo criterio F/G ya usado en
    // otras fases para distinguir lectura self-service de lectura de configuración administrativa.
    public static readonly RateLimitPolicyDefinition AuthMfaPolicyRead = Define(
        "auth.f.mfa_policy_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por GetRoles + GetPermissionsCatalog (RolesController).
    public static readonly RateLimitPolicyDefinition AuthRoleRead = Define(
        "auth.f.role_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // UsersController.GetUserById — lookup simple por id, sin filtros. GetUsers (búsqueda con
    // search+isActive) ya NO comparte esta política — ver AuthUserSearch (hallazgo #11).
    public static readonly RateLimitPolicyDefinition AuthUserRead = Define(
        "auth.f.user_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Auditoría post-Fase-9 (hallazgo #11) — UsersController.GetUsers acepta search+isActive:
    // búsqueda con filtros (H), no un lookup simple — se separa de auth.f.user_read (que
    // GetUserById conserva) en vez de forzar el cap de endpoint de Capa 4 sobre un lookup barato.
    public static readonly RateLimitPolicyDefinition AuthUserSearch = Define(
        "auth.h.user_search",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 100
    );

    public static readonly RateLimitPolicyDefinition AuthTenantLimitsRead = Define(
        "auth.f.tenant_limits_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition AuthMeRead = Define(
        "auth.f.me_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por List + Detail (OnboardingAdminController) — PlatformAdmin-only.
    public static readonly RateLimitPolicyDefinition AuthOnboardingAdminRead = Define(
        "auth.f.onboarding_admin_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por Create + Resend + Cancel (InvitationsController) — Accept queda exenta (ver
    // doc-comment de fase, anónimo, protegido por ILoginThrottler).
    public static readonly RateLimitPolicyDefinition AuthInvitationManage = Define(
        "auth.g.invitation_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por SessionsController (RevokeSession/RevokeAllMySessions) y AuthController
    // (Revoke/Logout) — mismo perfil de invalidación de sesión/token pese a vivir en 2 controllers.
    public static readonly RateLimitPolicyDefinition AuthSessionManage = Define(
        "auth.g.session_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por Create + Verify + Activate + Disable + ChangeSubdomain (TenantDomainsController).
    public static readonly RateLimitPolicyDefinition AuthTenantDomainManage = Define(
        "auth.g.tenant_domain_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition AuthTermsAccept = Define(
        "auth.g.terms_accept",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por ChangePassword + RequestEmailChange + RequestPhoneVerification +
    // ConfirmPhoneVerification (CredentialsController, todas autenticadas) — ForgotPassword/
    // ResetPassword/ConfirmEmailChange quedan exentas (anónimas, protección de dominio separada).
    public static readonly RateLimitPolicyDefinition AuthCredentialsManage = Define(
        "auth.g.credentials_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por SetupTotp + ConfirmTotp + Disable + RegenerateRecoveryCodes +
    // RevokeTrustedDevice (MfaController, todas autenticadas) — Verify queda exenta (paso 2 del
    // login, anónimo, sin JWT todavía).
    public static readonly RateLimitPolicyDefinition AuthMfaManage = Define(
        "auth.g.mfa_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition AuthMfaPolicyManage = Define(
        "auth.g.mfa_policy_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por Create + Update + SetPermissions + Deactivate (RolesController).
    public static readonly RateLimitPolicyDefinition AuthRoleManage = Define(
        "auth.g.role_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por Deactivate + Reactivate + AssignRoles (UsersController, sobre otro usuario).
    public static readonly RateLimitPolicyDefinition AuthUserManage = Define(
        "auth.g.user_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // UpdateMyProfile — sobre el propio usuario, política propia (cualquier actor autenticado, no
    // requiere UsersManage, a diferencia de auth.g.user_manage).
    public static readonly RateLimitPolicyDefinition AuthUserProfileManage = Define(
        "auth.g.user_profile_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Resume + UpdateAndResume + ForceComplete (OnboardingAdminController) — PlatformAdmin-only,
    // bajo volumen y alto impacto, política propia.
    public static readonly RateLimitPolicyDefinition AuthOnboardingAdminManage = Define(
        "auth.g.onboarding_admin_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Auditoría independiente post-Fase-9: CancelAndRefund dispara un reembolso Stripe real
    // (OnboardingRefundRequestedIntegrationEvent) — es literalmente "acción que mueve dinero", el
    // ejemplo textual de categoría M en §4, no G. Reclasificada + el handler ahora escribe
    // AuthAuditLog (invariante §4: incluso al 429, ver CancelAndRefundOnboardingAdminHandler).
    // Primera política M de Auth — mismo shape que payment_app.m.refund (partición Tenant|User con
    // fallback Tenant, como el resto de OnboardingAdminController, porque el "tenant" acá es el
    // PlatformTenant sentinel compartido por todo admin — sin el User de por medio un solo
    // PlatformAdmin abusivo no quedaría aislado de los demás).
    public static readonly RateLimitPolicyDefinition AuthOnboardingAdminCancelRefund = Define(
        "auth.m.onboarding_admin_cancel_refund",
        RateLimitCategory.M,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 5,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );

    // TermsVersionsController.Publish — PlatformAdmin-only, sube el documento legal vigente.
    public static readonly RateLimitPolicyDefinition AuthTermsVersionPublish = Define(
        "auth.g.terms_version_publish",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );
}
