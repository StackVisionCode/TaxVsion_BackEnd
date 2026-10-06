using TaxVision.Signature.Application.Profiles;
using TaxVision.Signature.Domain.Settings;
using Xunit;

namespace TaxVision.Signature.Tests.Application;

/// <summary>
/// F4 — <c>signature.sign_own</c>. Dos capas: kill-switch del tenant y permiso por-usuario.
/// Estos tests son sobre la policy pura (<see cref="SignatureVisibilityPolicy"/>); los handlers
/// que la usan ya están cubiertos en <see cref="SignatureProfileHandlersTests"/>.
/// </summary>
public sealed class SignatureSignOwnPermissionTests
{
    [Fact]
    public void Policy_blocks_everyone_when_tenant_kill_switch_is_off()
    {
        var settings = NewSettings(allowOwn: false);

        Assert.False(SignatureVisibilityPolicy.CanUsePersonal(actorIsAdmin: true, actorHasSignOwn: true, settings));
        Assert.False(SignatureVisibilityPolicy.CanUsePersonal(actorIsAdmin: false, actorHasSignOwn: true, settings));
    }

    [Fact]
    public void Policy_blocks_employee_without_permission_even_with_kill_switch_on()
    {
        var settings = NewSettings(allowOwn: true);

        Assert.False(SignatureVisibilityPolicy.CanUsePersonal(actorIsAdmin: false, actorHasSignOwn: false, settings));
    }

    [Fact]
    public void Policy_allows_employee_with_permission_when_kill_switch_is_on()
    {
        var settings = NewSettings(allowOwn: true);

        Assert.True(SignatureVisibilityPolicy.CanUsePersonal(actorIsAdmin: false, actorHasSignOwn: true, settings));
    }

    [Fact]
    public void Policy_allows_admin_without_explicit_permission_when_kill_switch_is_on()
    {
        var settings = NewSettings(allowOwn: true);

        Assert.True(SignatureVisibilityPolicy.CanUsePersonal(actorIsAdmin: true, actorHasSignOwn: false, settings));
    }

    [Fact]
    public void Policy_defaults_to_permissive_tenant_without_settings()
    {
        Assert.True(
            SignatureVisibilityPolicy.CanUsePersonal(actorIsAdmin: true, actorHasSignOwn: false, settings: null)
        );
        Assert.True(
            SignatureVisibilityPolicy.CanUsePersonal(actorIsAdmin: false, actorHasSignOwn: true, settings: null)
        );
        Assert.False(
            SignatureVisibilityPolicy.CanUsePersonal(actorIsAdmin: false, actorHasSignOwn: false, settings: null)
        );
    }

    private static TenantSignatureSettings NewSettings(bool allowOwn)
    {
        var settings = TenantSignatureSettings.CreateForNewTenant(Guid.NewGuid(), "secret").Value;
        if (!allowOwn)
            settings.DisableEmployeeOwnSignature();
        return settings;
    }
}
