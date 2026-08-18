using TaxVision.Auth.Domain.Onboarding.TermsVersions;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 6 (+ auditoría MinIO/legal-docs) — TermsVersion.Publish/SetContentUri.</summary>
public sealed class TermsVersionTests
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly string ValidHash = new('a', 64);

    [Fact]
    public void Publish_succeeds_with_valid_inputs()
    {
        var userId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var result = TermsVersion.Publish(
            TermsKind.TermsOfService,
            "2026-08-01",
            fileId,
            ValidHash,
            "en-US",
            userId,
            Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TermsKind.TermsOfService, result.Value.Kind);
        Assert.Equal("2026-08-01", result.Value.Version);
        Assert.Equal(fileId, result.Value.ContentFileId);
        Assert.Equal(ValidHash, result.Value.ContentHash);
        Assert.Null(result.Value.ContentUri);
        Assert.Equal("en-US", result.Value.Locale);
        Assert.Equal(userId, result.Value.CreatedByUserId);
        Assert.Equal(Now, result.Value.EffectiveFromUtc);
        Assert.Null(result.Value.EffectiveUntilUtc);
    }

    [Fact]
    public void Publish_uppercases_hash_and_stores_it_lowercase()
    {
        var result = TermsVersion.Publish(
            TermsKind.PrivacyPolicy,
            "2026-08-01",
            Guid.NewGuid(),
            new string('A', 64),
            "en-US",
            Guid.NewGuid(),
            Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(new string('a', 64), result.Value.ContentHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Publish_fails_for_missing_version(string? version)
    {
        var result = TermsVersion.Publish(
            TermsKind.TermsOfService,
            version!,
            Guid.NewGuid(),
            ValidHash,
            "en-US",
            Guid.NewGuid(),
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsVersionInvalid", result.Error.Code);
    }

    [Fact]
    public void Publish_fails_for_missing_content_file_id()
    {
        var result = TermsVersion.Publish(
            TermsKind.TermsOfService,
            "2026-08-01",
            Guid.Empty,
            ValidHash,
            "en-US",
            Guid.NewGuid(),
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsContentFileIdRequired", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Publish_fails_for_invalid_content_hash(string hash)
    {
        var result = TermsVersion.Publish(
            TermsKind.TermsOfService,
            "2026-08-01",
            Guid.NewGuid(),
            hash,
            "en-US",
            Guid.NewGuid(),
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsContentHashInvalid", result.Error.Code);
    }

    [Fact]
    public void Publish_fails_for_missing_locale()
    {
        var result = TermsVersion.Publish(
            TermsKind.TermsOfService,
            "2026-08-01",
            Guid.NewGuid(),
            ValidHash,
            "",
            Guid.NewGuid(),
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsLocaleInvalid", result.Error.Code);
    }

    [Fact]
    public void Publish_fails_for_empty_created_by_user_id()
    {
        var result = TermsVersion.Publish(
            TermsKind.TermsOfService,
            "2026-08-01",
            Guid.NewGuid(),
            ValidHash,
            "en-US",
            Guid.Empty,
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsCreatedByRequired", result.Error.Code);
    }

    [Fact]
    public void Publish_fails_when_effective_until_is_not_in_the_future()
    {
        var result = TermsVersion.Publish(
            TermsKind.TermsOfService,
            "2026-08-01",
            Guid.NewGuid(),
            ValidHash,
            "en-US",
            Guid.NewGuid(),
            Now,
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsEffectiveUntilInvalid", result.Error.Code);
    }

    [Fact]
    public void SetContentUri_succeeds_with_a_valid_uri()
    {
        var published = TermsVersion.Publish(
            TermsKind.TermsOfService,
            "2026-08-01",
            Guid.NewGuid(),
            ValidHash,
            "en-US",
            Guid.NewGuid(),
            Now
        );
        var version = published.Value;

        var result = version.SetContentUri($"/auth/onboarding/terms/{version.Id}/content");

        Assert.True(result.IsSuccess);
        Assert.Equal($"/auth/onboarding/terms/{version.Id}/content", version.ContentUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void SetContentUri_fails_for_missing_uri(string? uri)
    {
        var version = TermsVersion
            .Publish(TermsKind.TermsOfService, "2026-08-01", Guid.NewGuid(), ValidHash, "en-US", Guid.NewGuid(), Now)
            .Value;

        var result = version.SetContentUri(uri!);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TermsContentUriInvalid", result.Error.Code);
    }
}
