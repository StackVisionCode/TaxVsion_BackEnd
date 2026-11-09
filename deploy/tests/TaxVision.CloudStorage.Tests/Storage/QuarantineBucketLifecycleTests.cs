using Minio.DataModel.ILM;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Tests.Storage;

/// <summary>CST-18: la cuarentena vence solo lo que está bajo purged/, a los N días desde la purga.</summary>
public sealed class QuarantineBucketLifecycleTests
{
    [Fact]
    public void Purged_rule_marshals_to_a_prefix_filtered_expiration_with_the_configured_days()
    {
        var xml = new LifecycleConfiguration([QuarantineBucketLifecycle.PurgedRule(30)]).MarshalXML();

        Assert.Contains("<ID>taxvision-quarantine-purged</ID>", xml, StringComparison.Ordinal);
        Assert.Contains("<Status>Enabled</Status>", xml, StringComparison.Ordinal);
        Assert.Contains("<Days>30</Days>", xml, StringComparison.Ordinal);
        Assert.Contains("<Prefix>purged/</Prefix>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Tag>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("tenants/", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_keeps_existing_rules_and_adds_the_purged_rule()
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

        var merged = QuarantineBucketLifecycle.MergePurgedRule(new LifecycleConfiguration([existing]), 30);

        Assert.NotNull(merged);
        Assert.Equal(["ops-rule", QuarantineBucketLifecycle.PurgedRuleId], merged!.Rules.Select(r => r.ID).ToArray());
    }

    [Fact]
    public void Merge_is_a_no_op_when_the_rule_already_has_the_same_days()
    {
        var current = new LifecycleConfiguration([QuarantineBucketLifecycle.PurgedRule(30)]);

        Assert.Null(QuarantineBucketLifecycle.MergePurgedRule(current, 30));
    }

    [Fact]
    public void Merge_replaces_the_rule_when_the_retention_changes()
    {
        var current = new LifecycleConfiguration([QuarantineBucketLifecycle.PurgedRule(30)]);

        var merged = QuarantineBucketLifecycle.MergePurgedRule(current, 45);

        Assert.NotNull(merged);
        Assert.Equal(45, Assert.Single(merged!.Rules).Expiration.Days);
    }

    [Fact]
    public void Merge_replaces_the_previous_tag_filtered_rule_with_the_prefix_filter()
    {
        // La versión de CST-5 filtraba tenants/ + kind=purged y contaba los días desde la cuarentena.
        var legacy = new LifecycleRule(
            abortIncompleteMultipartUpload: null,
            id: QuarantineBucketLifecycle.PurgedRuleId,
            expiration: new Expiration { Days = 30 },
            transition: null,
            filter: new RuleFilter
            {
                TheAndOperator = new AndOperator("tenants/", new Dictionary<string, string> { ["kind"] = "purged" }),
            },
            noncurrentVersionExpiration: null,
            noncurrentVersionTransition: null,
            status: LifecycleRule.LifecycleRuleStatusEnabled
        );

        var merged = QuarantineBucketLifecycle.MergePurgedRule(new LifecycleConfiguration([legacy]), 30);

        Assert.NotNull(merged);
        var rule = Assert.Single(merged!.Rules);
        Assert.Equal("purged/", rule.Filter.Prefix);
        Assert.Null(rule.Filter.TheAndOperator);
    }
}
