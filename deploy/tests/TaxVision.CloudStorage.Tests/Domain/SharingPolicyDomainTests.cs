using TaxVision.CloudStorage.Domain.Platform;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Tests.Domain;

/// <summary>
/// Reglas de compartir de la oficina (vida, contraseña, intentos, descargas), bloqueo de un enlace por
/// contraseñas fallidas y rangos de los ajustes de plataforma.
/// </summary>
public sealed class SharingPolicyDomainTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static TenantStorageLimit Limit() => TenantStorageLimit.Create(Guid.NewGuid(), "pro", 1000, 1000);

    [Theory]
    [InlineData(10, 5)] // por defecto mayor que la máxima
    [InlineData(0, 30)]
    [InlineData(7, 3651)]
    public void The_default_lifetime_must_fit_inside_the_maximum(int defaultDays, int maxDays)
    {
        var result = Limit().SetSharingPolicy(true, false, defaultDays, maxDays, 8, 10, null);

        Assert.Equal(QuotaErrors.InvalidSharingPolicy, result.Error);
    }

    [Theory]
    [InlineData(3, 10, null)]
    [InlineData(8, 2, null)]
    [InlineData(8, 10, 0)]
    public void Password_length_attempts_and_downloads_have_sane_ranges(int minLength, int attempts, int? downloads)
    {
        var result = Limit().SetSharingPolicy(true, false, 7, 30, minLength, attempts, downloads);

        Assert.Equal(QuotaErrors.InvalidSharingPolicy, result.Error);
    }

    [Fact]
    public void A_short_password_is_rejected_on_any_link()
    {
        var limit = Limit();
        limit.SetSharingPolicy(true, false, 7, 30, 10, 10, null);

        Assert.Equal("ShareLink.PasswordTooShort", limit.CheckSharePassword("short").Error.Code);
        Assert.True(limit.CheckSharePassword(null).IsSuccess);
    }

    [Fact]
    public void Required_password_applies_to_public_links_too_but_not_to_external_recipients()
    {
        var limit = Limit();
        limit.SetSharingPolicy(true, true, 7, 30, 8, 10, null);

        Assert.Equal(
            ShareErrors.PasswordRequiredForLinkShare,
            limit.CheckNewExternalShare(ShareVisibility.Public, null, null, null, Now).Error
        );
        Assert.True(limit.CheckNewExternalShare(ShareVisibility.ExternalRecipients, null, null, null, Now).IsSuccess);
    }

    [Fact]
    public void An_external_link_cannot_outlive_or_outcount_the_office_caps()
    {
        var limit = Limit();
        limit.SetSharingPolicy(true, false, 7, 30, 8, 10, 5);

        Assert.Equal(
            ShareErrors.ShareLifetimeExceedsMax,
            limit.CheckNewExternalShare(ShareVisibility.ExternalRecipients, null, Now.AddDays(31), null, Now).Error
        );
        Assert.Equal(
            "ShareLink.DownloadsExceedMax",
            limit.CheckNewExternalShare(ShareVisibility.ExternalLink, null, null, 6, Now).Error.Code
        );
        Assert.True(limit.CheckNewExternalShare(ShareVisibility.ExternalLink, null, Now.AddDays(30), 5, Now).IsSuccess);
    }

    [Fact]
    public void A_link_locks_after_the_office_attempts_and_a_correct_password_clears_the_count()
    {
        var link = PasswordLink();

        link.RegisterWrongPassword(3, TimeSpan.FromMinutes(15), Now);
        link.RegisterWrongPassword(3, TimeSpan.FromMinutes(15), Now);
        link.RegisterCorrectPassword();
        link.RegisterWrongPassword(3, TimeSpan.FromMinutes(15), Now);
        link.RegisterWrongPassword(3, TimeSpan.FromMinutes(15), Now);
        Assert.False(link.IsPasswordLocked(Now));

        link.RegisterWrongPassword(3, TimeSpan.FromMinutes(15), Now);

        Assert.True(link.IsPasswordLocked(Now.AddMinutes(14)));
        Assert.False(link.IsPasswordLocked(Now.AddMinutes(15)));
    }

    [Fact]
    public void A_link_without_a_chosen_date_lives_the_office_default()
    {
        var (link, _) = ShareLink
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ShareResourceType.File,
                ShareVisibility.ExternalLink,
                SharePermission.Download,
                null,
                null,
                null,
                Guid.NewGuid(),
                Now,
                defaultLifetimeDays: 3
            )
            .Value;

        Assert.Equal(Now.AddDays(3), link.ExpiresAtUtc);
    }

    [Theory]
    [InlineData(25, 30)] // la reserva no puede pasar la vida del bucket temporal
    [InlineData(24, 0)]
    public void Platform_settings_reject_values_out_of_range(int reservationHours, int recycleBinDays)
    {
        var settings = StoragePlatformSettings.Defaults();

        var result = settings.Update(5, 2, reservationHours, recycleBinDays, 500, 50, 2L * 1024 * 1024 * 1024, 15, Guid.NewGuid(), Now);

        Assert.Equal(StoragePlatformSettingsErrors.OutOfRange, result.Error);
    }

    private static ShareLink PasswordLink() =>
        ShareLink
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ShareResourceType.File,
                ShareVisibility.Public,
                SharePermission.View,
                "hashed:secret-pass",
                Now.AddDays(1),
                null,
                Guid.NewGuid(),
                Now
            )
            .Value.Link;
}
