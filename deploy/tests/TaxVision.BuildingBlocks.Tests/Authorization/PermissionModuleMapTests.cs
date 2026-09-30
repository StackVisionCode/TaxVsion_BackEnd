using BuildingBlocks.Authorization;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Authorization;

/// <summary>
/// Fundación del gate de módulo (Entitlements en runtime): mapeo permiso→módulo validado contra el
/// catálogo de planes. Un permiso sin módulo es siempre efectivo (no se gatea) → nunca 403 falso.
/// </summary>
public sealed class PermissionModuleMapTests
{
    [Theory]
    [InlineData("customers.view", "customers")]
    [InlineData("customers.import", "customers")]
    [InlineData("customers.fiscal_profile.reveal", "customers")]
    [InlineData("signature.request.create", "signatures")] // módulo plural, permiso singular
    [InlineData("signature.legal.manage", "signatures")]
    [InlineData("documents.branding.manage", "documents")]
    [InlineData("cloudstorage.file.view", "documents")]
    [InlineData("scribe.templates.write", "documents")]
    [InlineData("calendar.read", "planner")]
    [InlineData("reminders.read", "planner")]
    [InlineData("tasks.read", "planner")]
    [InlineData("notes.read", "planner")]
    [InlineData("correspondence.read", "email")]
    [InlineData("correspondence.manage", "email")]
    [InlineData("connectors.accounts.write", "email")]
    [InlineData("postmaster.messages.read", "email")]
    [InlineData("email.use", "email")]
    [InlineData("communication.chat.start", "comms")]
    [InlineData("comms.calls", "comms")]
    [InlineData("campaigns.manage", "campaigns")]
    [InlineData("reports.view", "reports")]
    public void Maps_permission_to_its_module(string permissionCode, string expectedModule)
    {
        Assert.Equal(expectedModule, PermissionModuleMap.ModuleFor(permissionCode));
        Assert.True(PermissionModuleMap.IsModuleGated(permissionCode));
    }

    [Theory]
    // Transversales / sin módulo → siempre efectivos (nunca 403 falso).
    [InlineData("sms.send")]
    [InlineData("notification.settings.manage")]
    [InlineData("codes.code.read")] // Growth
    [InlineData("roles.manage")]
    [InlineData("users.manage")]
    [InlineData("subscription.plan.change")]
    [InlineData("billing.view")]
    [InlineData("invoicing.view")] // facturación tenant→cliente: operativa, siempre efectiva
    [InlineData("invoicing.manage")]
    [InlineData("seats.manage")]
    [InlineData("tenant.status.change")]
    [InlineData("onboarding.admin.manage")]
    // Módulos comerciales SIN permisos backend → no gatean nada (features frontend/futuras).
    [InlineData("marketing.something")]
    [InlineData("builder.something")]
    [InlineData("irs.something")]
    [InlineData("miles.something")]
    public void Leaves_transversal_or_unmapped_permissions_ungated(string permissionCode)
    {
        Assert.Null(PermissionModuleMap.ModuleFor(permissionCode));
        Assert.False(PermissionModuleMap.IsModuleGated(permissionCode));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Null_or_empty_is_ungated(string? permissionCode)
    {
        Assert.Null(PermissionModuleMap.ModuleFor(permissionCode!));
        Assert.False(PermissionModuleMap.IsModuleGated(permissionCode!));
    }

    // ---------- A6/A5.2: exenciones del gate ----------

    [Theory]
    // Están bajo el prefijo `communication.` —y por tanto el mapa los llevaría a `comms`— pero
    // ninguno ES la feature que se vende con ese módulo.
    [InlineData("communication.notification.read")]
    [InlineData("communication.support.open")]
    [InlineData("communication.support.agent")]
    public void The_exempt_permissions_are_not_gated_by_comms(string permissionCode)
    {
        Assert.Null(PermissionModuleMap.ModuleFor(permissionCode));
        Assert.False(PermissionModuleMap.IsModuleGated(permissionCode));
    }

    // ---------- El split comms / meetings ----------

    [Theory]
    [InlineData("communication.meeting.create")]
    [InlineData("communication.meeting.join")]
    [InlineData("communication.meeting.host")]
    [InlineData("communication.meeting.record")]
    public void Meetings_are_their_own_module(string permissionCode)
    {
        // Las reuniones se venden aparte (Pro y Enterprise); chat, llamadas y vídeo van en todos los
        // planes. Si esto devolviera "comms", Starter se llevaría las reuniones gratis.
        Assert.Equal("meetings", PermissionModuleMap.ModuleFor(permissionCode));
    }

    [Fact]
    public void The_more_specific_prefix_wins_over_the_general_one()
    {
        // `communication.meeting.` y `communication.` son el ÚNICO par de prefijos que se solapa, y el
        // mapa se evalúa en orden. Invertirlos no rompe ninguna compilación: simplemente manda las
        // reuniones a `comms` en silencio. Este test es lo que lo impide.
        Assert.Equal("meetings", PermissionModuleMap.ModuleFor("communication.meeting.create"));
        Assert.Equal("comms", PermissionModuleMap.ModuleFor("communication.chat.start"));
        Assert.Equal("comms", PermissionModuleMap.ModuleFor("communication.call.start"));
        Assert.Equal("comms", PermissionModuleMap.ModuleFor("communication.videocall.start"));
    }

    [Fact]
    public void Exempting_does_not_open_the_rest_of_the_comms_module()
    {
        // El bug que una exención mal hecha produce: relajar el prefijo entero y regalar el chat.
        Assert.Equal("comms", PermissionModuleMap.ModuleFor("communication.chat.start"));
        Assert.Equal("comms", PermissionModuleMap.ModuleFor("communication.call.start"));
        Assert.Equal("comms", PermissionModuleMap.ModuleFor("communication.settings.manage"));
        // Y las reuniones siguen gateadas por SU módulo, no por ninguno de los dos caminos de escape.
        Assert.Equal("meetings", PermissionModuleMap.ModuleFor("communication.meeting.create"));
    }

    [Fact]
    public void The_exempt_list_matches_the_permission_constants()
    {
        // La lista se escribe con las constantes, no con literales: si alguien renombra el código en
        // CommunicationPermissions, esto lo sigue. El test existe para que la lista no crezca sin que
        // se note — cada entrada nueva es una decisión de producto, no un detalle.
        Assert.Equal(3, PermissionModuleMap.Exempt.Count);
        Assert.Contains(CommunicationPermissions.NotificationRead, PermissionModuleMap.Exempt);
        Assert.Contains(CommunicationPermissions.SupportOpen, PermissionModuleMap.Exempt);
        Assert.Contains(CommunicationPermissions.SupportAgent, PermissionModuleMap.Exempt);
    }

    [Fact]
    public void An_exempt_permission_is_effective_for_a_tenant_without_the_module()
    {
        // La razón de ser de la exención, en los términos del techo de plan: el mismo cálculo que
        // usan el gate, el techo y el bootstrap `/auth/me/effective-access`. Sin la exención, un
        // Starter (que tiene `documents` pero no `comms`) perdería la campanita de TODO.
        var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "documents", "planner" };

        static bool Effective(string code, HashSet<string> modules)
        {
            var module = PermissionModuleMap.ModuleFor(code);
            return module is null || modules.Contains(module);
        }

        Assert.True(Effective("communication.notification.read", enabled));
        Assert.True(Effective("communication.support.open", enabled));
        Assert.False(Effective("communication.chat.start", enabled));
    }
}
