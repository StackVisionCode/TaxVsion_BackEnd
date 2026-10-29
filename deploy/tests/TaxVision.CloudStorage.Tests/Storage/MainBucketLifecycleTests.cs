using Minio.DataModel.ILM;
using Minio.Exceptions;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Tests.Storage;

/// <summary>
/// Fotos de perfil F0 — regla de lifecycle del bucket principal versionado: las versiones no actuales
/// con kind=avatar expiran al día. Se verifica el XML que el SDK de MinIO (7.0.0) manda de verdad.
/// </summary>
public sealed class MainBucketLifecycleTests
{
    [Fact]
    public void Avatar_rule_marshals_to_a_tag_filtered_noncurrent_version_expiration_of_one_day()
    {
        var xml = new LifecycleConfiguration([MainBucketLifecycle.AvatarRule()]).MarshalXML();

        Assert.Contains("<ID>taxvision-avatars-noncurrent-1d</ID>", xml, StringComparison.Ordinal);
        Assert.Contains("<Status>Enabled</Status>", xml, StringComparison.Ordinal);
        Assert.Contains("<NoncurrentDays>1</NoncurrentDays>", xml, StringComparison.Ordinal);
        Assert.Contains("<Prefix>tenants/</Prefix>", xml, StringComparison.Ordinal);
        Assert.Contains("<Key>kind</Key>", xml, StringComparison.Ordinal);
        Assert.Contains("<Value>avatar</Value>", xml, StringComparison.Ordinal);
        // Nunca expira la versión ACTUAL: eso borraría avatares vivos.
        Assert.DoesNotContain("<Expiration>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_on_a_bucket_without_lifecycle_creates_only_the_avatar_rule()
    {
        var merged = MainBucketLifecycle.MergeAvatarRule(null);

        Assert.NotNull(merged);
        Assert.Equal(MainBucketLifecycle.AvatarRuleId, Assert.Single(merged!.Rules).ID);
    }

    [Fact]
    public void Merge_keeps_rules_that_operations_already_configured()
    {
        var existing = new LifecycleRule(
            abortIncompleteMultipartUpload: null,
            id: "ops-rule",
            expiration: new Expiration { Days = 365 },
            transition: null,
            filter: new RuleFilter { Prefix = "exports/" },
            noncurrentVersionExpiration: null,
            noncurrentVersionTransition: null,
            status: LifecycleRule.LifecycleRuleStatusEnabled
        );

        var merged = MainBucketLifecycle.MergeAvatarRule(new LifecycleConfiguration([existing]));

        Assert.NotNull(merged);
        Assert.Equal(["ops-rule", MainBucketLifecycle.AvatarRuleId], merged!.Rules.Select(r => r.ID).ToArray());
    }

    [Fact]
    public void Merge_is_a_no_op_when_the_avatar_rule_is_already_in_place()
    {
        var current = new LifecycleConfiguration([MainBucketLifecycle.AvatarRule()]);

        Assert.Null(MainBucketLifecycle.MergeAvatarRule(current));
    }

    [Fact]
    public void Merge_replaces_a_stale_avatar_rule()
    {
        var stale = new LifecycleRule(
            abortIncompleteMultipartUpload: null,
            id: MainBucketLifecycle.AvatarRuleId,
            expiration: null,
            transition: null,
            filter: null,
            noncurrentVersionExpiration: new NoncurrentVersionExpiration(30, null),
            noncurrentVersionTransition: null,
            status: LifecycleRule.LifecycleRuleStatusEnabled
        );

        var merged = MainBucketLifecycle.MergeAvatarRule(new LifecycleConfiguration([stale]));

        var rule = Assert.Single(merged!.Rules);
        Assert.Equal(1u, rule.NoncurrentVersionExpirationObject!.NoncurrentDays);
    }

    [Theory]
    [InlineData("NoSuchLifecycleConfiguration", true)]
    [InlineData("AccessDenied", false)]
    [InlineData(null, false)]
    public void Only_a_missing_configuration_is_treated_as_empty(string? code, bool expected)
    {
        Assert.Equal(expected, MainBucketLifecycle.IsMissingConfiguration(code));
    }

    [Fact]
    public void The_sdk_exception_for_a_bucket_without_lifecycle_is_treated_as_empty()
    {
        // Medido contra MinIO local: el SDK 7.0.0 lanza UnexpectedMinioException, no ErrorResponseException.
        Assert.True(
            MainBucketLifecycle.IsMissingConfiguration(
                new UnexpectedMinioException(
                    "MinIO API responded with message=The lifecycle configuration does not exist"
                )
            )
        );
        Assert.False(MainBucketLifecycle.IsMissingConfiguration(new UnexpectedMinioException("Access Denied.")));
    }
}
