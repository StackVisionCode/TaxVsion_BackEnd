using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Tests.Domain;

/// <summary>Fase C3 — ShareLink: creacion, ciclo de vida, recipients y ShareToken.</summary>
public sealed class ShareLinkDomainTests
{
    private static (ShareLink Link, string PlainToken) CreateValid(
        DateTime? nowUtc = null,
        DateTime? expiresAtUtc = null,
        int? maxAccessCount = null,
        ShareVisibility visibility = ShareVisibility.Public
    )
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var result = ShareLink.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShareResourceType.File,
            visibility,
            SharePermission.Download,
            null,
            expiresAtUtc,
            maxAccessCount,
            Guid.NewGuid(),
            now
        );
        return result.Value;
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Create_rejects_IsRecursive_or_AppliesToFutureItems_on_a_File_link(
        bool isRecursive,
        bool appliesToFutureItems
    )
    {
        var result = ShareLink.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShareResourceType.File,
            ShareVisibility.TenantOnly,
            SharePermission.View,
            null,
            null,
            null,
            Guid.NewGuid(),
            DateTime.UtcNow,
            isRecursive,
            appliesToFutureItems
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ShareErrors.RecursiveOnlyForFolders, result.Error);
    }

    [Fact]
    public void Create_allows_IsRecursive_and_AppliesToFutureItems_on_a_Folder_link()
    {
        var result = ShareLink.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShareResourceType.Folder,
            ShareVisibility.TenantOnly,
            SharePermission.View,
            null,
            null,
            null,
            Guid.NewGuid(),
            DateTime.UtcNow,
            isRecursive: true,
            appliesToFutureItems: true
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Link.IsRecursive);
        Assert.True(result.Value.Link.AppliesToFutureItems);
    }

    [Fact]
    public void Create_defaults_to_a_seven_day_expiration_when_none_is_given()
    {
        var now = DateTime.UtcNow;
        var (link, plainToken) = CreateValid(now);

        Assert.Equal(now.AddDays(ShareLink.DefaultLifetimeDays), link.ExpiresAtUtc);
        Assert.NotEmpty(plainToken);
        Assert.Equal(ShareStatus.Active, link.Status);
        Assert.Equal(0, link.AccessCount);
    }

    [Fact]
    public void Create_rejects_an_expiration_in_the_past()
    {
        var now = DateTime.UtcNow;
        var result = ShareLink.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShareResourceType.File,
            ShareVisibility.TenantOnly,
            SharePermission.View,
            null,
            now.AddMinutes(-1),
            null,
            Guid.NewGuid(),
            now
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ShareErrors.ExpirationInPast, result.Error);
    }

    [Fact]
    public void Create_rejects_a_zero_or_negative_max_access_count()
    {
        var result = ShareLink.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShareResourceType.File,
            ShareVisibility.TenantOnly,
            SharePermission.View,
            null,
            null,
            0,
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ShareErrors.InvalidMaxAccessCount, result.Error);
    }

    [Fact]
    public void RegisterAccess_marks_the_link_exhausted_once_it_reaches_max_access_count()
    {
        var (link, _) = CreateValid(maxAccessCount: 2);
        var now = DateTime.UtcNow;

        link.RegisterAccess(now);
        Assert.Equal(ShareStatus.Active, link.Status);

        link.RegisterAccess(now);
        Assert.Equal(ShareStatus.Exhausted, link.Status);
        Assert.Equal(2, link.AccessCount);
    }

    [Fact]
    public void IsUsable_is_false_once_expired_even_while_Status_is_still_Active()
    {
        var now = DateTime.UtcNow;
        var (link, _) = CreateValid(now, now.AddMinutes(5));

        Assert.True(link.IsUsable(now));
        Assert.False(link.IsUsable(now.AddMinutes(10)));
    }

    [Fact]
    public void Revoke_fails_when_the_link_is_already_revoked()
    {
        var (link, _) = CreateValid();
        var now = DateTime.UtcNow;

        Assert.True(link.Revoke(now).IsSuccess);
        var second = link.Revoke(now);

        Assert.True(second.IsFailure);
        Assert.Equal(ShareErrors.AlreadyRevoked, second.Error);
    }

    [Fact]
    public void UpdateExpiration_rejects_a_new_date_in_the_past()
    {
        var (link, _) = CreateValid();
        var now = DateTime.UtcNow;

        var result = link.UpdateExpiration(now.AddMinutes(-1), now);

        Assert.True(result.IsFailure);
        Assert.Equal(ShareErrors.ExpirationInPast, result.Error);
    }

    [Fact]
    public void EffectiveStatus_is_Revoked_once_the_link_is_revoked()
    {
        var (link, _) = CreateValid();
        var now = DateTime.UtcNow;

        link.Revoke(now);

        Assert.Equal(ShareLinkEffectiveStatus.Revoked, link.EffectiveStatus(now));
    }

    [Fact]
    public void EffectiveStatus_is_Exhausted_once_access_count_reaches_the_limit()
    {
        var (link, _) = CreateValid(maxAccessCount: 1);
        var now = DateTime.UtcNow;

        link.RegisterAccess(now);

        Assert.Equal(ShareLinkEffectiveStatus.Exhausted, link.EffectiveStatus(now));
    }

    [Fact]
    public void EffectiveStatus_is_Expired_once_ExpiresAtUtc_has_passed_even_though_Status_is_still_Active()
    {
        var now = DateTime.UtcNow;
        var (link, _) = CreateValid(now, now.AddMinutes(1));

        Assert.Equal(ShareStatus.Active, link.Status);
        Assert.Equal(ShareLinkEffectiveStatus.Expired, link.EffectiveStatus(now.AddMinutes(5)));
    }

    [Fact]
    public void EffectiveStatus_is_Active_while_none_of_the_above_apply()
    {
        var now = DateTime.UtcNow;
        var (link, _) = CreateValid(now, now.AddDays(1));

        Assert.Equal(ShareLinkEffectiveStatus.Active, link.EffectiveStatus(now));
    }

    [Fact]
    public void Recipients_are_added_and_checked_by_kind_independently()
    {
        var (link, _) = CreateValid(visibility: ShareVisibility.SpecificUsers);
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        link.AddUserRecipient(userId);
        link.AddCustomerRecipient(customerId);
        link.AddExternalRecipient("  Someone@Example.com  ");

        Assert.True(link.HasUserRecipient(userId));
        Assert.False(link.HasUserRecipient(Guid.NewGuid()));
        Assert.True(link.HasCustomerRecipient(customerId));
        Assert.True(link.HasEmailRecipient("someone@example.com"));
        Assert.True(link.HasAnyRecipient);
        Assert.Equal(3, link.Recipients.Count);
    }

    [Fact]
    public void ShareToken_HashOf_is_deterministic_and_matches_the_hash_produced_at_Create()
    {
        var token = ShareToken.Create();

        Assert.Equal(token.Hash, ShareToken.HashOf(token.Value));
        Assert.Equal(4, token.Last4.Length);
        Assert.EndsWith(token.Last4, token.Value, StringComparison.Ordinal);
    }
}
