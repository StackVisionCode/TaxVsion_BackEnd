using BuildingBlocks.Authorization;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>
/// Fase A0 — cubre los metadatos nuevos del catálogo (MinPlanTier/IsAssignableByTenant) y la
/// reconciliación de los 18 permisos de Communication que antes solo existían en BD.
/// </summary>
public sealed class PermissionCatalogTests
{
    [Fact]
    public void Saas_payment_refund_is_platform_only_and_never_reaches_a_tenant_role()
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == PermissionCatalog.PaymentAppSaaSPaymentRefund);

        Assert.True(definition.PlatformOnly);
        Assert.False(definition.IsAssignableByTenant);
        Assert.Equal(
            new[] { UserActorType.PlatformAdmin },
            Permission.InferAllowedActorTypes(false, definition.PlatformOnly)
        );
        Assert.DoesNotContain(
            PermissionCatalog.PaymentAppSaaSPaymentRefund,
            PermissionCatalog.SystemTenantAdminRootPermissions()
        );
        Assert.DoesNotContain(
            PermissionCatalog.PaymentAppSaaSPaymentRefund,
            PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee)
        );
    }

    // RolePermissionGuard solo lee IsAssignableByTenant: esta regla es la que impide que un TA meta
    // una permission de plataforma o peligrosa en un custom role.
    [Fact]
    public void Platform_only_and_dangerous_permissions_are_never_assignable_by_a_tenant()
    {
        var violations = PermissionCatalog
            .All.Where(d => (d.PlatformOnly || d.IsDangerous) && d.IsAssignableByTenant)
            .Select(d => d.Code)
            .ToArray();

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData(PermissionCatalog.BillingView)]
    [InlineData(PermissionCatalog.BillingManage)]
    [InlineData(PermissionCatalog.SubscriptionManage)]
    [InlineData(PermissionCatalog.RolesManage)]
    public void Reserved_permissions_are_never_assignable_by_a_tenant(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);

        Assert.False(definition.IsAssignableByTenant);
    }

    [Theory]
    [InlineData(PermissionCatalog.CustomersView)]
    [InlineData(PermissionCatalog.CloudStorageFileUpload)]
    [InlineData(PermissionCatalog.SignatureRequestCreate)]
    public void Baseline_permissions_are_assignable_from_the_starter_plan(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);

        Assert.True(definition.IsAssignableByTenant);
        Assert.Equal((int)PlanTier.Starter, definition.MinPlanTier);
    }

    [Theory]
    [InlineData(PermissionCatalog.CampaignsManage)]
    public void Pro_only_modules_require_at_least_the_pro_tier(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);

        Assert.Equal((int)PlanTier.Pro, definition.MinPlanTier);
    }

    [Fact]
    public void Communication_permissions_are_reconciled_into_the_catalog_with_the_original_guids()
    {
        // Los mismos 18 GUID que sembró la migración AddCommunicationPermissions por SQL
        // directo (2026-07-10) — deben resolver a los mismos Ids, nunca duplicarse. La capa de
        // plataforma (213–215) llegó después con sus propios GUID y se siembra por HasData.
        var originalIds = Enumerable.Range(45, 18).Select(n => new Guid($"a1000000-0000-0000-0000-{n:D12}")).ToList();
        var platformIds = Enumerable.Range(213, 3).Select(n => new Guid($"a1000000-0000-0000-0000-{n:D12}")).ToList();

        var actualIds = PermissionCatalog
            .All.Where(definition => definition.Module == "communication")
            .Select(definition => definition.Id)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal(originalIds.Concat(platformIds).OrderBy(id => id), actualIds);
    }

    /// <summary>
    /// El techo de plan (§27) decide qué puede OTORGAR un tenant a un rol propio. Desde que `comms`
    /// (chat, llamadas y vídeo) entró en Starter, dejar esos permisos en Pro daba un Starter con chat
    /// incluido en el plan y sin poder delegarlo a nadie. Las reuniones se venden aparte, así que sus
    /// cuatro permisos sí siguen en Pro.
    /// </summary>
    [Fact]
    public void Only_the_meeting_permissions_still_require_the_pro_tier()
    {
        var communication = PermissionCatalog.All.Where(definition => definition.Module == "communication").ToList();

        var proOnly = communication
            .Where(definition => definition.MinPlanTier == (int)PlanTier.Pro)
            .Select(definition => definition.Code)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                PermissionCatalog.CommunicationMeetingCreate,
                PermissionCatalog.CommunicationMeetingHost,
                PermissionCatalog.CommunicationMeetingJoin,
                PermissionCatalog.CommunicationMeetingRecord,
            ],
            proOnly
        );

        // El resto, alcanzable desde Starter.
        Assert.All(
            communication.Where(definition => !proOnly.Contains(definition.Code)),
            definition => Assert.Equal((int)PlanTier.Starter, definition.MinPlanTier)
        );
    }

    /// <summary>
    /// Las descripciones son texto de INTERFAZ: el cajón de accesos del CRM las pinta debajo del código
    /// del permiso, y la regla del producto es que todo el copy que ve el usuario va en inglés.
    ///
    /// El test mira acentos y verbos españoles frecuentes, no traduce: basta para que una descripción
    /// nueva escrita en español no llegue a producción sin que nadie lo note, que es exactamente lo que
    /// pasó con las 194 originales.
    /// </summary>
    [Fact]
    public void Every_description_is_written_in_english()
    {
        string[] spanishVerbs =
        [
            "Ver ",
            "Gestionar",
            "Crear",
            "Consultar",
            "Enviar",
            "Eliminar",
            "Borrar",
            "Cambiar",
            "Activar",
            "Revocar",
            "Asignar",
            "Emitir",
            "Definir",
            "Abrir",
            "Iniciar",
            "Adjuntar",
            "Responder",
            "Archivar",
            "Descargar",
            "Subir",
            "Restaurar",
            "Editar",
            "Invitar",
            "Comprar",
            "del tenant",
        ];

        var offenders = PermissionCatalog
            .All.Where(definition =>
                definition.Description.Any(character => "áéíóúñ¿¡".Contains(character, StringComparison.Ordinal))
                || spanishVerbs.Any(verb => definition.Description.Contains(verb, StringComparison.OrdinalIgnoreCase))
            )
            .Select(definition => $"{definition.Code}: {definition.Description}")
            .ToList();

        Assert.True(offenders.Count == 0, "Descripciones que no están en inglés: " + string.Join(" | ", offenders));
    }

    [Fact]
    public void Employee_defaults_now_include_the_reconciled_communication_permissions()
    {
        var defaults = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);

        Assert.Contains(PermissionCatalog.CommunicationChatStart, defaults);
        Assert.Contains(PermissionCatalog.CommunicationMeetingJoin, defaults);
        Assert.DoesNotContain(PermissionCatalog.CommunicationSettingsManage, defaults);
        Assert.DoesNotContain(PermissionCatalog.CommunicationAnalyticsRead, defaults);
    }

    [Fact]
    public void Employee_defaults_include_the_daily_operational_catalog_inventory_and_sms_permissions()
    {
        var defaults = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);

        Assert.Contains(PermissionCatalog.CatalogRead, defaults);
        Assert.Contains(PermissionCatalog.CatalogWrite, defaults);
        Assert.Contains(PermissionCatalog.CatalogDelete, defaults);
        Assert.Contains(PermissionCatalog.InventoryRead, defaults);
        Assert.Contains(PermissionCatalog.InventoryWrite, defaults);
        Assert.Contains(PermissionCatalog.InventoryAdjust, defaults);
        Assert.Contains(PermissionCatalog.SmsSend, defaults);
    }

    // connect_own = conectar/administrar el buzón PERSONAL propio (no el de oficina, que es write).
    // Llega al empleado por defecto (para que pueda usar su propio correo sin depender del admin);
    // el admin puede restringirlo por usuario desde Edit access. accounts.write NO llega al empleado.
    [Fact]
    public void Employee_defaults_include_connect_own_and_office_read_but_not_accounts_write()
    {
        var defaults = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);

        Assert.Contains(PermissionCatalog.ConnectorsAccountsRead, defaults);
        Assert.Contains(PermissionCatalog.ConnectorsAccountsConnectOwn, defaults);
        Assert.Contains(PermissionCatalog.ConnectorsAccountsOfficeRead, defaults);
        Assert.DoesNotContain(PermissionCatalog.ConnectorsAccountsWrite, defaults);
    }

    /// <summary>
    /// Facturación tenant→cliente (invoicing.*): a DIFERENCIA de billing.* (suscripción SaaS, peligroso/
    /// admin-only), es operativa — no peligrosa, asignable, y llega tanto al empleado (bundle explícito)
    /// como al TenantAdmin (bundle automático de no-peligrosos).
    /// </summary>
    [Theory]
    [InlineData(PermissionCatalog.InvoicingView)]
    [InlineData(PermissionCatalog.InvoicingManage)]
    public void Invoicing_permissions_are_operational_not_dangerous_and_reach_employee_and_admin(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);
        var employeeDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.False(definition.IsDangerous);
        Assert.True(definition.IsAssignableByTenant);
        Assert.Equal((int)PlanTier.Starter, definition.MinPlanTier);
        Assert.Contains(code, employeeDefaults);
        Assert.Contains(code, tenantAdminDefaults);
    }

    [Fact]
    public void Customer_portal_defaults_never_include_moderation_or_admin_communication_permissions()
    {
        var defaults = PermissionCatalog.SystemRoleDefaults(Role.SystemCustomerPortal);

        Assert.Contains(PermissionCatalog.CommunicationChatStart, defaults);
        Assert.DoesNotContain(PermissionCatalog.CommunicationChatModerate, defaults);
        Assert.DoesNotContain(PermissionCatalog.CommunicationMeetingHost, defaults);
        Assert.DoesNotContain(PermissionCatalog.CommunicationSettingsManage, defaults);
    }

    [Fact]
    public void Growth_cross_tenant_permission_is_platform_only_and_never_in_tenant_admin_defaults()
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == PermissionCatalog.GrowthAdminCrossTenant);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.True(definition.PlatformOnly);
        Assert.False(definition.IsAssignableByTenant);
        Assert.DoesNotContain(PermissionCatalog.GrowthAdminCrossTenant, tenantAdminDefaults);
    }

    [Fact]
    public void Platform_branding_permission_is_platform_only_and_never_in_tenant_admin_defaults()
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == PermissionCatalog.PlatformBrandingManage);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.True(definition.PlatformOnly);
        Assert.False(definition.IsAssignableByTenant);
        Assert.Equal("branding", definition.Module);
        // Propiedad de seguridad central: solo el PlatformAdmin gestiona la marca del sistema; el
        // TenantAdmin nunca la recibe por el bundle de su rol.
        Assert.DoesNotContain(PermissionCatalog.PlatformBrandingManage, tenantAdminDefaults);
    }

    [Fact]
    public void Alert_subscriptions_allow_tenant_admin_but_are_excluded_from_admin_bundle()
    {
        // D1 (2026-10-19): un co-TenantAdmin puede recibir estos 4 permisos por asignacion
        // explicita, pero no se les otorga por defecto en el bundle del rol de sistema
        // ni en el set root del admin principal. El owner sigue recibiendo lo obligatorio
        // via TenantOwnerAudience (hardcode al TenantAdmin mas antiguo).
        var alertCodes = new[]
        {
            PermissionCatalog.NotificationsBillingAlerts,
            PermissionCatalog.NotificationsStorageAlerts,
            PermissionCatalog.NotificationsSecurityTenantAlerts,
            PermissionCatalog.NotificationsMailboxAlerts,
        };
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);
        var tenantAdminRoot = PermissionCatalog.SystemTenantAdminRootPermissions();

        foreach (var code in alertCodes)
        {
            var definition = PermissionCatalog.All.Single(d => d.Code == code);
            Assert.True(definition.ExcludeFromAdminBundle, $"{code}: ExcludeFromAdminBundle debe ser true");
            Assert.Contains(UserActorType.TenantAdmin, definition.EffectiveAllowedActorTypes);
            Assert.Contains(UserActorType.TenantEmployee, definition.EffectiveAllowedActorTypes);
            Assert.DoesNotContain(code, tenantAdminDefaults);
            Assert.DoesNotContain(code, tenantAdminRoot);
        }
    }

    [Fact]
    public void Tenant_branding_permission_stays_assignable_to_the_tenant()
    {
        // Contraparte: la marca PROPIA del tenant sí es del TenantAdmin (no PlatformOnly).
        var definition = PermissionCatalog.All.Single(d => d.Code == PermissionCatalog.BrandingManage);

        Assert.False(definition.PlatformOnly);
        Assert.Equal("branding", definition.Module);
    }

    [Fact]
    public void Growth_permissions_have_unique_codes_and_ids()
    {
        var growth = PermissionCatalog
            .All.Where(definition => definition.Module is "growth" or "codes" or "referrals")
            .ToArray();

        // CAT-F0 — Fase 0 removió los permisos IsReserved del módulo growth/codes/referrals.
        // Quedan los 6 que un endpoint real exige.
        Assert.Equal(6, growth.Length);
        Assert.Equal(growth.Length, growth.Select(definition => definition.Code).Distinct().Count());
        Assert.Equal(growth.Length, growth.Select(definition => definition.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("starter", PlanTier.Starter)]
    [InlineData("PRO", PlanTier.Pro)]
    [InlineData("Enterprise", PlanTier.Enterprise)]
    [InlineData(null, PlanTier.Starter)]
    [InlineData("unknown-plan", PlanTier.Starter)]
    public void PlanTierResolver_fails_closed_to_starter_for_unknown_or_missing_plan_codes(
        string? planCode,
        PlanTier expected
    )
    {
        Assert.Equal(expected, PlanTierResolver.FromPlanCode(planCode));
    }

    // RBAC Fase 2 (RBAC_Hardening_Plan.md) — SystemTenantAdmin deja de ser un god-role dinámico.

    [Theory]
    [InlineData(PermissionCatalog.RolesManage)]
    [InlineData(PermissionCatalog.BillingView)]
    [InlineData(PermissionCatalog.BillingManage)]
    [InlineData(PermissionCatalog.SubscriptionManage)]
    [InlineData(PermissionCatalog.TenantDomainsManage)]
    [InlineData(PermissionCatalog.SignaturePlanConstraintsManage)]
    [InlineData(PermissionCatalog.CloudStorageLegalManage)]
    public void SystemTenantAdmin_does_not_include_dangerous_permissions(string dangerousCode)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == dangerousCode);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.True(definition.IsDangerous);
        Assert.DoesNotContain(dangerousCode, tenantAdminDefaults);
    }

    [Fact]
    public void SystemTenantAdmin_still_includes_a_baseline_non_dangerous_permission()
    {
        // Guardarraíl del guardarraíl: confirma que el filtro !IsDangerous no vació el set entero
        // por error — un permiso operativo normal debe seguir llegando por el bundle automático.
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.Contains(PermissionCatalog.CustomersView, tenantAdminDefaults);
        Assert.Contains(PermissionCatalog.CustomersManage, tenantAdminDefaults);
    }

    /// <summary>
    /// Las comms de doble uso están marcadas IsCustomerPortal=true pero declaran AllowedActorTypes
    /// explícito incluyendo TenantAdmin. El filtro AllowedActorTypes.Contains(TenantAdmin) de los dos
    /// sets del admin las incluye por la vía declarativa, sin el hack TenantAdminCommunicationExtras
    /// retirado. Un Owner debe poder iniciar chats, unirse a reuniones y leer sus notificaciones.
    /// </summary>
    [Theory]
    [InlineData(PermissionCatalog.CommunicationChatStart)]
    [InlineData(PermissionCatalog.CommunicationChatReply)]
    [InlineData(PermissionCatalog.CommunicationMeetingJoin)]
    [InlineData(PermissionCatalog.CommunicationScreenshotCreate)]
    [InlineData(PermissionCatalog.CommunicationNotificationRead)]
    public void SystemTenantAdmin_includes_the_dual_use_communication_permissions(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);

        Assert.True(definition.IsCustomerPortal);
        Assert.Contains(code, PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin));
        Assert.Contains(code, PermissionCatalog.SystemTenantAdminRootPermissions());
    }

    [Fact]
    public void SystemTenantAdmin_communication_bundle_stays_deduped_and_scoped()
    {
        // Guardarraíl: el .Concat no introduce duplicados, y re-agregar las de doble uso no
        // arrastra permisos portal-only genuinos (PortalFoldersView es del cliente, no del Owner).
        var defaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.Equal(defaults.Count, defaults.Distinct().Count());
        Assert.DoesNotContain(PermissionCatalog.PortalFoldersView, defaults);
    }

    // ---------- Fase A3: baseline del empleado, split de Campaigns y permisos reservados ----------

    /// <summary>
    /// El empleado recibía 403 en Notes, en la lista de plantillas de firma, al cancelar su propia
    /// solicitud, al armar un grupo de chat y en todo Campaigns — trabajo diario, no administración.
    /// </summary>
    [Theory]
    [InlineData(PermissionCatalog.NotesRead)]
    [InlineData(PermissionCatalog.NotesManage)]
    [InlineData(PermissionCatalog.SignatureRequestCancel)]
    [InlineData(PermissionCatalog.CommunicationGroupCreate)]
    [InlineData(PermissionCatalog.CampaignsView)]
    [InlineData(PermissionCatalog.CampaignsManage)]
    [InlineData(PermissionCatalog.CampaignsSend)]
    public void Employee_defaults_include_the_daily_work_that_used_to_return_403(string code)
    {
        Assert.Contains(code, PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee));
    }

    /// <summary>Lo que sigue siendo administrativo no entra al bundle del empleado.</summary>
    [Theory]
    [InlineData(PermissionCatalog.CampaignsSendersManage)]
    [InlineData(PermissionCatalog.InvoicingIssuerManage)]
    [InlineData(PermissionCatalog.NotesViewAll)]
    public void Employee_defaults_stay_out_of_the_administrative_permissions(string code)
    {
        var employeeDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);
        Assert.DoesNotContain(code, employeeDefaults);
        // Y el admin raíz sí las tiene: separar no es quitarle nada a nadie.
        Assert.Contains(code, PermissionCatalog.SystemTenantAdminRootPermissions());
    }

    [Fact]
    public void The_four_campaigns_permissions_share_the_module_and_the_pro_tier()
    {
        string[] codes =
        [
            PermissionCatalog.CampaignsView,
            PermissionCatalog.CampaignsManage,
            PermissionCatalog.CampaignsSend,
            PermissionCatalog.CampaignsSendersManage,
        ];

        foreach (var code in codes)
        {
            var definition = PermissionCatalog.All.Single(d => d.Code == code);
            Assert.Equal("campaigns", definition.Module);
            Assert.Equal((int)PlanTier.Pro, definition.MinPlanTier);
            Assert.True(definition.IsAssignableByTenant);
            Assert.False(definition.IsDangerous);
        }
    }

    /// <summary>
    /// §R.6: el permiso que el administrador reconoce en el cajón de accesos del cliente entra al
    /// bundle del rol de portal. Sin esto, aplicarlo en las rutas de llamada dejaría sin llamadas a
    /// todos los clientes que ya existen.
    /// </summary>
    [Fact]
    public void Portal_calls_use_is_in_the_customer_portal_bundle()
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == PermissionCatalog.PortalCallsUse);

        Assert.True(definition.IsCustomerPortal);
        Assert.False(definition.IsReserved);
        Assert.Contains(
            PermissionCatalog.PortalCallsUse,
            PermissionCatalog.SystemRoleDefaults(Role.SystemCustomerPortal)
        );
    }

    /// <summary>
    /// Fitness: un permiso reservado no protege nada todavía, así que no puede ser asignable ni
    /// aparecer en ningún bundle. Si alguien marca uno como reservado y se olvida del otro flag,
    /// esto falla.
    /// </summary>
    [Fact]
    public void A_reserved_permission_is_never_assignable_and_never_in_a_bundle()
    {
        var reserved = PermissionCatalog.All.Where(d => d.IsReserved).Select(d => d.Code).ToArray();

        Assert.All(
            reserved,
            code =>
                Assert.False(
                    PermissionCatalog.All.Single(d => d.Code == code).IsAssignableByTenant,
                    $"{code} está reservado pero sigue siendo asignable por el tenant."
                )
        );

        string[] bundles = [Role.SystemTenantAdmin, Role.SystemEmployee, Role.SystemCustomerPortal];
        foreach (var bundle in bundles)
        {
            var defaults = PermissionCatalog.SystemRoleDefaults(bundle);
            foreach (var code in reserved)
                Assert.DoesNotContain(code, defaults);
        }

        foreach (var code in reserved)
            Assert.DoesNotContain(code, PermissionCatalog.SystemTenantAdminRootPermissions());
    }

    [Fact]
    public void The_catalog_has_no_duplicate_codes_or_ids()
    {
        var codes = PermissionCatalog.All.Select(d => d.Code).ToArray();
        var ids = PermissionCatalog.All.Select(d => d.Id).ToArray();

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void DmcaManage_is_platform_only_and_never_reaches_a_tenant_role()
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == PermissionCatalog.CloudStorageDmcaManage);

        Assert.True(definition.PlatformOnly);
        Assert.False(definition.IsAssignableByTenant);
        Assert.Equal(
            new[] { UserActorType.PlatformAdmin },
            Permission.InferAllowedActorTypes(false, definition.PlatformOnly)
        );
        Assert.DoesNotContain(
            PermissionCatalog.CloudStorageDmcaManage,
            PermissionCatalog.SystemTenantAdminRootPermissions()
        );
        Assert.DoesNotContain(
            PermissionCatalog.CloudStorageDmcaManage,
            PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin)
        );
        Assert.DoesNotContain(
            PermissionCatalog.CloudStorageDmcaManage,
            PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee)
        );
    }

    /// <summary>
    /// Regresión de §R.7: separar el DMCA no le quita al admin raíz el legal hold que ya tenía
    /// (retener evidencia de un litigio propio sigue siendo un caso de uso del tenant), ni le
    /// devuelve el permiso al bundle de creación de roles custom.
    /// </summary>
    [Fact]
    public void LegalManage_stays_with_the_tenant_root_admin_after_splitting_the_dmca()
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == PermissionCatalog.CloudStorageLegalManage);

        Assert.False(definition.PlatformOnly);
        Assert.True(definition.IsDangerous);
        Assert.False(definition.IsAssignableByTenant);
        Assert.Contains(
            PermissionCatalog.CloudStorageLegalManage,
            PermissionCatalog.SystemTenantAdminRootPermissions()
        );
    }

    [Fact]
    public void DmcaCounterNotice_is_deliberately_not_dangerous_despite_looking_like_a_legal_permission()
    {
        // Desviación deliberada del plan original (ver comentario junto a la definición en
        // PermissionCatalog): a diferencia de CloudStorageLegalManage (equipo legal de
        // plataforma), este permiso es la respuesta legal del propio tenant a un takedown
        // recibido sobre SU archivo — con plazos reales de 17 U.S.C. §512(g). Marcarlo
        // IsDangerous dejaría a cualquier oficina sin poder auto-defenderse sin depender de
        // PlatformAdmin.
        var definition = PermissionCatalog.All.Single(d => d.Code == PermissionCatalog.CloudStorageDmcaCounterNotice);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.False(definition.IsDangerous);
        Assert.Contains(PermissionCatalog.CloudStorageDmcaCounterNotice, tenantAdminDefaults);
    }

    // verbo≠permiso: la purga de correspondencia y el legal hold de firma dejaron de vivir bajo un
    // permiso de lectura. Ambos son gestión admin-only: llegan a TenantAdmin por el bundle automático
    // pero NO al empleado (no están en la lista explícita de SystemEmployee).
    [Theory]
    [InlineData(PermissionCatalog.CorrespondenceManage)]
    [InlineData(PermissionCatalog.SignatureLegalManage)]
    [InlineData(PermissionCatalog.CustomersImport)]
    public void Management_permissions_reach_tenant_admin_but_not_employees_by_default(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);
        var employeeDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);

        Assert.False(definition.IsDangerous);
        Assert.False(definition.PlatformOnly);
        Assert.Contains(code, tenantAdminDefaults);
        Assert.DoesNotContain(code, employeeDefaults);
    }

    // RBAC Fase 8 (RBAC_Hardening_Plan.md) — migración de [Authorize(Roles=...)] a
    // [HasPermission] en Subscription/Auth-Invitations/Tenant. Los TenantAdmin-only de antes
    // (subscription.plan.change, seats.manage, addons.manage) deben seguir llegando por el
    // bundle automático — la migración exige "más permisivo, no bloquea injustamente", así que
    // el caso positivo (siguen en el default) es tan importante como el negativo (PlatformOnly
    // queda afuera).

    [Theory]
    [InlineData(PermissionCatalog.SubscriptionPlanChange)]
    [InlineData(PermissionCatalog.SeatsManage)]
    [InlineData(PermissionCatalog.SeatSlotGatingManage)]
    [InlineData(PermissionCatalog.AddOnsManage)]
    public void Subscription_tenant_scoped_permissions_reach_tenant_admin_by_default(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.False(definition.PlatformOnly);
        Assert.False(definition.IsDangerous);
        Assert.Contains(code, tenantAdminDefaults);
    }

    [Theory]
    [InlineData(PermissionCatalog.SubscriptionSuspend)]
    [InlineData(PermissionCatalog.SubscriptionReactivate)]
    [InlineData(PermissionCatalog.SubscriptionRenew)]
    [InlineData(PermissionCatalog.SubscriptionAdminCrossTenant)]
    [InlineData(PermissionCatalog.TenantStatusChange)]
    [InlineData(PermissionCatalog.TenantListView)]
    public void Subscription_and_tenant_platform_only_permissions_never_reach_tenant_admin_defaults(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.True(definition.PlatformOnly);
        Assert.False(definition.IsAssignableByTenant);
        Assert.DoesNotContain(code, tenantAdminDefaults);
    }

    [Fact]
    public void Fase8_permissions_have_unique_codes_and_ids()
    {
        var fase8 = PermissionCatalog
            .All.Where(definition => definition.Module is "subscription" or "seats" or "addons")
            .ToArray();

        // Cinco de subscription, dos de seats y uno de addons. audit.view se reutiliza.
        Assert.Equal(8, fase8.Length);
        Assert.Equal(fase8.Length, fase8.Select(definition => definition.Code).Distinct().Count());
        Assert.Equal(fase8.Length, fase8.Select(definition => definition.Id).Distinct().Count());

        var tenantModule = PermissionCatalog.All.Where(definition => definition.Module == "tenant").ToArray();
        Assert.Equal(2, tenantModule.Length);
        Assert.Equal(tenantModule.Length, tenantModule.Select(definition => definition.Code).Distinct().Count());
    }

    /// <summary>
    /// Task Fase 3 — fija la decisión de reparto del bundle, que es lo único de este módulo que no
    /// se puede leer del propio código sin ambigüedad. <c>tasks.assign</c> SÍ entra por defecto (el
    /// flujo estrella es «preparar → revisión interna», y sin él no existe el día uno);
    /// <c>manage_all</c> y <c>templates.manage</c> NO (override de supervisión y configuración de la
    /// firma, ambos por rol explícito).
    /// </summary>
    [Fact]
    public void Employee_defaults_include_task_assign_but_never_the_supervisor_override()
    {
        var defaults = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);

        Assert.Contains(PermissionCatalog.TasksRead, defaults);
        Assert.Contains(PermissionCatalog.TasksWrite, defaults);
        Assert.Contains(PermissionCatalog.TasksAssign, defaults);
        Assert.DoesNotContain(PermissionCatalog.TasksManageAll, defaults);
        Assert.DoesNotContain(PermissionCatalog.TasksTemplatesManage, defaults);
    }

    /// <summary>
    /// Ningún permiso de Task llega al portal del cliente: la lista de tareas es trabajo interno de
    /// la firma. Lo que el cliente recibe sale por Notification, no por un endpoint de Task.
    ///
    /// <para>
    /// Se afirma sobre el <see cref="Permission"/> sembrado, no sobre el
    /// <see cref="PermissionCatalog.PermissionDefinition"/>: en el record del catálogo
    /// <c>AllowedActorTypes</c> es <b>null</b> cuando no se declara explícito — la inferencia corre
    /// dentro de <see cref="Permission.Seed"/>. Afirmar sobre el record medía otra cosa que la que
    /// enforza el filtro.
    /// </para>
    /// </summary>
    [Fact]
    public void Only_the_portal_task_permission_reaches_the_customer_portal()
    {
        var seeded = PermissionCatalog
            .All.Where(definition => definition.Module == "tasks")
            .Select(definition =>
                Permission.Seed(
                    definition.Id,
                    definition.Code,
                    definition.Module,
                    definition.Description,
                    definition.IsCustomerPortal,
                    definition.MinPlanTier,
                    definition.IsAssignableByTenant,
                    definition.PlatformOnly,
                    definition.AllowedActorTypes,
                    definition.IsDangerous
                )
            )
            .ToArray();

        // Solo las superficies explicitas del portal tienen al cliente como destinatario.
        var portal = Array.FindAll(seeded, permission => permission.IsCustomerPortal);
        Assert.Equal(2, portal.Length);
        Assert.Contains(portal, permission => permission.Code == TasksPermissions.PortalClientRequests);
        Assert.Contains(portal, permission => permission.Code == TasksPermissions.PortalForms);

        foreach (var permission in seeded)
        {
            if (permission.IsCustomerPortal)
                continue;

            Assert.DoesNotContain(UserActorType.CustomerPortal, permission.AllowedActorTypes);
        }

        var portalTaskDefaults = PermissionCatalog
            .SystemRoleDefaults(Role.SystemCustomerPortal)
            .Where(code => code.StartsWith("tasks.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal([TasksPermissions.PortalClientRequests, TasksPermissions.PortalForms], portalTaskDefaults);
    }

    /// <summary>
    /// §R.7 — A1/A7 hicieron explícitos dos permisos de Signature que el empleado YA ejercia por otra
    /// vía: "My Signature" bastaba con request.create y extender el vencimiento con request.resend. Si
    /// el bundle no los trae, el día del despliegue todos los preparadores pierden las dos cosas.
    /// </summary>
    [Fact]
    public void The_employee_keeps_the_signature_work_he_could_already_do()
    {
        var employee = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);

        Assert.Contains(SignaturePermissions.PreparerManage, employee);
        Assert.Contains(SignaturePermissions.RequestExpire, employee);
    }

    // CloudStorage per-item access management (Fase 2) — dos permisos nuevos: manage_assigned
    // (delegable al preparador, acotado a sus clientes asignados) y manage_any (admin-only, no
    // delegable, mismo patrón que cloudstorage.share.manage).

    [Fact]
    public void Customer_access_manage_assigned_is_delegable_to_the_employee_but_not_in_the_default_bundle()
    {
        var definition = PermissionCatalog.All.Single(d =>
            d.Code == PermissionCatalog.CloudStorageCustomerAccessManageAssigned
        );
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);
        var employeeDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);
        var portalDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemCustomerPortal);

        Assert.Equal("cloudstorage", definition.Module);
        Assert.True(definition.IsAssignableByTenant);
        Assert.False(definition.IsDangerous);
        Assert.False(definition.PlatformOnly);
        Assert.False(definition.IsReserved);
        Assert.False(definition.IsCustomerPortal);
        Assert.Equal(new[] { UserActorType.TenantAdmin, UserActorType.TenantEmployee }, definition.AllowedActorTypes);
        // El admin raíz lo recibe por el bundle automático; el empleado se lo da el admin con un rol
        // custom cuando corresponde (no viene por defecto, mismo criterio que CustomersImport o
        // CorrespondenceManage).
        Assert.Contains(PermissionCatalog.CloudStorageCustomerAccessManageAssigned, tenantAdminDefaults);
        Assert.DoesNotContain(PermissionCatalog.CloudStorageCustomerAccessManageAssigned, employeeDefaults);
        Assert.DoesNotContain(PermissionCatalog.CloudStorageCustomerAccessManageAssigned, portalDefaults);
    }

    [Fact]
    public void Customer_access_manage_any_is_tenant_admin_only_and_never_delegable()
    {
        var definition = PermissionCatalog.All.Single(d =>
            d.Code == PermissionCatalog.CloudStorageCustomerAccessManageAny
        );
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);
        var employeeDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);
        var portalDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemCustomerPortal);

        Assert.Equal("cloudstorage", definition.Module);
        // Mismo patrón que CloudStorageShareManage: no delegable a un rol custom; solo llega al rol
        // de sistema Tenant Admin por SystemRoleDefaults.
        Assert.False(definition.IsAssignableByTenant);
        Assert.False(definition.IsDangerous);
        Assert.False(definition.PlatformOnly);
        Assert.False(definition.IsReserved);
        // PlatformAdmin se incluye para soporte sobre un tenant (mismo criterio que NotesViewAll).
        Assert.Equal(new[] { UserActorType.TenantAdmin, UserActorType.PlatformAdmin }, definition.AllowedActorTypes);
        Assert.Contains(PermissionCatalog.CloudStorageCustomerAccessManageAny, tenantAdminDefaults);
        Assert.DoesNotContain(PermissionCatalog.CloudStorageCustomerAccessManageAny, employeeDefaults);
        Assert.DoesNotContain(PermissionCatalog.CloudStorageCustomerAccessManageAny, portalDefaults);
    }

    [Theory]
    [InlineData(PermissionCatalog.CloudStorageCustomerAccessManageAssigned)]
    [InlineData(PermissionCatalog.CloudStorageCustomerAccessManageAny)]
    public void Customer_access_permissions_are_gated_by_the_documents_module(string code)
    {
        // Guardarraíl del gate: todos los cloudstorage.* caen bajo el módulo "documents" (ver
        // PermissionModuleMap). Si el prefijo cambia por error, el gate pararía de aplicarles el techo.
        Assert.Equal("documents", PermissionModuleMap.ModuleFor(code));
        Assert.True(PermissionModuleMap.IsModuleGated(code));
    }

    [Fact]
    public void Client_form_permissions_have_stable_unique_metadata()
    {
        string[] codes =
        [
            TasksPermissions.FormTemplatesRead,
            TasksPermissions.FormTemplatesManage,
            TasksPermissions.FormTemplatesPublish,
            TasksPermissions.FormRequestsRead,
            TasksPermissions.FormRequestsManage,
            TasksPermissions.FormRequestsReview,
            TasksPermissions.FormRequestsExport,
            TasksPermissions.FormRequestsApplyProfile,
            TasksPermissions.FormSubmissionsReveal,
            TasksPermissions.FormsLibraryManage,
            TasksPermissions.FormsPlatformManage,
            TasksPermissions.PortalForms,
            TasksPermissions.FormTemplatesConfigureActions,
            TasksPermissions.FormRequestsSendExternal,
            TasksPermissions.FormIntakeLinksManage,
            TasksPermissions.FormConsentsManage,
        ];

        var definitions = PermissionCatalog.All.Where(definition => codes.Contains(definition.Code)).ToArray();

        Assert.Equal(16, definitions.Length);
        Assert.Equal(16, definitions.Select(definition => definition.Id).Distinct().Count());
        Assert.All(
            definitions,
            definition =>
            {
                Assert.Equal("tasks", definition.Module);
                Assert.Equal("planner", definition.GatedModule);
                Assert.False(string.IsNullOrWhiteSpace(definition.Description));
                Assert.EndsWith(".", definition.Description);
                Assert.False(definition.IsDangerous);
            }
        );

        string[] activeCodes =
        [
            TasksPermissions.FormTemplatesRead,
            TasksPermissions.FormTemplatesManage,
            TasksPermissions.FormTemplatesPublish,
            TasksPermissions.FormRequestsManage,
            TasksPermissions.FormsPlatformManage,
            TasksPermissions.PortalForms,
            TasksPermissions.FormRequestsRead,
            TasksPermissions.FormRequestsReview,
            TasksPermissions.FormSubmissionsReveal,
            TasksPermissions.FormRequestsSendExternal,
            TasksPermissions.FormIntakeLinksManage,
            TasksPermissions.FormConsentsManage,
            TasksPermissions.FormTemplatesConfigureActions,
            TasksPermissions.FormRequestsExport,
            TasksPermissions.FormRequestsApplyProfile,
        ];
        Assert.Equal(
            activeCodes.Order(),
            definitions.Where(item => !item.IsReserved).Select(item => item.Code).Order()
        );
        Assert.Equal(1, definitions.Count(item => item.IsReserved));
    }

    [Fact]
    public void Client_form_employee_bundle_only_contains_enforced_operational_permissions()
    {
        var employee = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);
        string[] operational =
        [
            TasksPermissions.FormTemplatesRead,
            TasksPermissions.FormTemplatesManage,
            TasksPermissions.FormTemplatesPublish,
            TasksPermissions.FormRequestsManage,
        ];

        Assert.All(operational, code => Assert.Contains(code, employee));
        Assert.DoesNotContain(TasksPermissions.FormRequestsExport, employee);
        Assert.DoesNotContain(TasksPermissions.FormRequestsApplyProfile, employee);
        Assert.DoesNotContain(TasksPermissions.FormSubmissionsReveal, employee);
        Assert.DoesNotContain(TasksPermissions.FormTemplatesConfigureActions, employee);
        Assert.DoesNotContain(TasksPermissions.FormIntakeLinksManage, employee);
        Assert.DoesNotContain(TasksPermissions.FormConsentsManage, employee);
    }

    [Fact]
    public void Client_form_platform_permissions_never_reach_tenant_bundles()
    {
        string[] platformCodes = [TasksPermissions.FormsLibraryManage, TasksPermissions.FormsPlatformManage];
        string[] tenantRoles = [Role.SystemTenantAdmin, Role.SystemEmployee, Role.SystemCustomerPortal];

        foreach (var code in platformCodes)
        {
            var definition = PermissionCatalog.All.Single(item => item.Code == code);
            Assert.True(definition.PlatformOnly);
            Assert.False(definition.IsAssignableByTenant);
            Assert.All(tenantRoles, role => Assert.DoesNotContain(code, PermissionCatalog.SystemRoleDefaults(role)));
            Assert.DoesNotContain(code, PermissionCatalog.SystemTenantAdminRootPermissions());
        }
    }

    [Fact]
    public void Client_form_portal_permission_only_reaches_customer_portal()
    {
        var portal = PermissionCatalog.SystemRoleDefaults(Role.SystemCustomerPortal);
        var employee = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);
        var tenantAdmin = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.Contains(TasksPermissions.PortalForms, portal);
        Assert.DoesNotContain(TasksPermissions.PortalForms, employee);
        Assert.DoesNotContain(TasksPermissions.PortalForms, tenantAdmin);
    }

    [Fact]
    public void Tenant_admin_receives_active_staff_permissions_but_not_reserved_ones()
    {
        var tenantAdmin = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);
        var staffDefinitions = PermissionCatalog.All.Where(definition =>
            definition.Code.StartsWith("tasks.form_", StringComparison.Ordinal)
            && !definition.PlatformOnly
            && !definition.IsCustomerPortal
        );

        Assert.Equal(13, staffDefinitions.Count());
        Assert.Equal(13, staffDefinitions.Count(definition => !definition.IsReserved));
        Assert.Equal(0, staffDefinitions.Count(definition => definition.IsReserved));
        // El preparador revisa y envía a externos; revelar, enlaces públicos y avisos son del admin.
        var employee = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);
        Assert.Contains(TasksPermissions.FormRequestsRead, employee);
        Assert.Contains(TasksPermissions.FormRequestsReview, employee);
        Assert.Contains(TasksPermissions.FormRequestsSendExternal, employee);
        Assert.DoesNotContain(TasksPermissions.FormSubmissionsReveal, employee);
        Assert.DoesNotContain(TasksPermissions.FormIntakeLinksManage, employee);
        Assert.DoesNotContain(TasksPermissions.FormConsentsManage, employee);
        Assert.DoesNotContain(TasksPermissions.FormTemplatesConfigureActions, employee);
        Assert.All(
            staffDefinitions,
            definition => Assert.Equal(!definition.IsReserved, tenantAdmin.Contains(definition.Code))
        );
    }

    /// <summary>
    /// La capa de configuración de plataforma de Communication y los topes de subida de CloudStorage
    /// son PlatformOnly: sin caso de uso tenant-propio, ningún rol de tenant los recibe por defecto ni
    /// un rol custom puede incluirlos (mismo criterio que signature.constraints.manage).
    /// </summary>
    [Theory]
    [InlineData(PermissionCatalog.CommunicationPlatformManage)]
    [InlineData(PermissionCatalog.CommunicationConstraintsManage)]
    [InlineData(PermissionCatalog.CommunicationPlatformAnalyticsRead)]
    [InlineData(PermissionCatalog.CloudStorageUploadPolicyManage)]
    [InlineData(PermissionCatalog.CloudStorageModerationManage)]
    public void Platform_configuration_permissions_are_platform_only_and_never_reach_a_tenant_role(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);

        Assert.True(definition.PlatformOnly);
        Assert.False(definition.IsAssignableByTenant);
        Assert.Equal(new[] { UserActorType.PlatformAdmin }, definition.EffectiveAllowedActorTypes);
        Assert.DoesNotContain(code, PermissionCatalog.SystemTenantAdminRootPermissions());
        Assert.DoesNotContain(code, PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin));
        Assert.DoesNotContain(code, PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee));
        Assert.DoesNotContain(code, PermissionCatalog.SystemRoleDefaults(Role.SystemCustomerPortal));
    }
}
