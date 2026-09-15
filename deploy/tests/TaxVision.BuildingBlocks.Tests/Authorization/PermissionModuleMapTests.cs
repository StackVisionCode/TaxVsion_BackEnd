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
}
