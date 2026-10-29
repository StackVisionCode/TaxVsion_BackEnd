using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.DataCollection.Engine.Tests;

public sealed class MappingValidatorTests
{
    private const string Create = """
        {"key":"create_client","type":"createCustomer","trigger":"onSubmit","config":{"kind":{"constant":"Individual"},"language":{"constant":"En"}}}
        """;
    private const string Individual = """
        {"id":"fd_first0001","type":"text","label":"First name","required":true,"props":{"maxLength":80}},
        {"id":"fd_last00001","type":"text","label":"Last name","required":true,"props":{"maxLength":80}},
        {"id":"fd_email0001","type":"email","label":"Email","required":true,"props":{}}
        """;
    private const string IndividualMappings = """
        {"componentId":"fd_first0001","targetKey":"customer.person.first_name"},
        {"componentId":"fd_last00001","targetKey":"customer.person.last_name"},
        {"componentId":"fd_email0001","targetKey":"customer.primary_email","transform":"lowercase"}
        """;

    [Fact]
    public void Minimal_individual_onboarding_is_structurally_valid_and_covered()
    {
        var definition = Parse(Individual, IndividualMappings, Create);

        Assert.True(EngineTestFactory.Create().ValidateForPublish(definition).IsValid);
        Assert.Empty(Validate(definition));
    }

    [Fact]
    public void Retired_target_is_reported_as_unknown()
    {
        var retired = MappingTestCatalog.Make(
            "customer.person.middle_name",
            MappingTargetType.String,
            80,
            status: MappingTargetStatus.Retired
        );
        var definition = Parse(
            Individual + """,{"id":"fd_middle001","type":"text","label":"Middle name","props":{"maxLength":80}}""",
            IndividualMappings + """,{"componentId":"fd_middle001","targetKey":"customer.person.middle_name"}""",
            Create
        );

        var issues = Validate(definition, MappingTestCatalog.Snapshot(retired));

        Assert.Equal([(IssueCodes.MappingUnknownTarget, "/mappings/3/targetKey")], Codes(issues));
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("signatureStep")]
    [InlineData("checkboxGroup")]
    [InlineData("currency")]
    public void Non_mappable_component_types_are_rejected(string type)
    {
        Assert.False(MappingCompatibility.IsCompatible(type, MappingTargetType.String));
        Assert.DoesNotContain(
            Enum.GetValues<MappingTargetType>(),
            targetType => MappingCompatibility.IsCompatible(type, targetType)
        );
    }

    [Theory]
    [InlineData("yesNo")]
    [InlineData("checkbox")]
    public void Boolean_sources_map_to_boolean_targets(string type) =>
        Assert.True(MappingCompatibility.IsCompatible(type, MappingTargetType.Boolean));

    [Fact]
    public void Repeater_row_field_inside_mapping_must_belong_to_the_repeater()
    {
        var definition = Parse(
            Individual,
            IndividualMappings
                + """,{"componentId":"sc_deps0001","targetKey":"customer.relation.dependents","rowFields":{"fd_first0001":"first_name","fd_deplast01":"last_name"}}""",
            Create,
            withDependents: true
        );

        var issues = Validate(definition);

        Assert.Contains((IssueCodes.MappingTypeMismatch, "/mappings/3/rowFields/fd_first0001"), Codes(issues));
        Assert.Contains((IssueCodes.MappingRequiredTargetMissing, "/mappings/3/rowFields"), Codes(issues));
    }

    [Fact]
    public void Enum_row_fields_cannot_be_fed_without_an_option_map()
    {
        var definition = Parse(
            Individual,
            IndividualMappings
                + """,{"componentId":"sc_deps0001","targetKey":"customer.relation.dependents","rowFields":{"fd_depfirst1":"first_name","fd_deplast01":"last_name","fd_deprel001":"relationship"}}""",
            Create,
            withDependents: true
        );

        Assert.Equal(
            [(IssueCodes.MappingTypeMismatch, "/mappings/3/rowFields/fd_deprel001")],
            Codes(Validate(definition))
        );
    }

    [Fact]
    public void Repeater_component_cannot_be_mapped_directly()
    {
        var definition = Parse(
            Individual,
            IndividualMappings + """,{"componentId":"fd_depfirst1","targetKey":"customer.person.middle_name"}""",
            Create,
            withDependents: true
        );

        Assert.Equal([(IssueCodes.MappingTypeMismatch, "/mappings/3/componentId")], Codes(Validate(definition)));
    }

    [Fact]
    public void Non_repeater_section_cannot_feed_a_collection()
    {
        var definition = Parse(
            Individual,
            IndividualMappings
                + """,{"componentId":"sc_abcdefgh","targetKey":"customer.relation.dependents","rowFields":{"fd_first0001":"first_name"}}""",
            Create
        );

        Assert.Equal([(IssueCodes.MappingTypeMismatch, "/mappings/3/componentId")], Codes(Validate(definition)));
    }

    [Fact]
    public void Option_map_on_a_text_target_is_a_type_mismatch()
    {
        var definition = Parse(
            Individual + """,{"id":"fd_middle001","type":"text","label":"Middle name","props":{"maxLength":80}}""",
            IndividualMappings
                + """,{"componentId":"fd_middle001","targetKey":"customer.person.middle_name","optionMap":{"a":"b"}}""",
            Create
        );

        Assert.Equal([(IssueCodes.MappingTypeMismatch, "/mappings/3/optionMap")], Codes(Validate(definition)));
    }

    [Fact]
    public void Kind_mapped_through_in_condition_counts_as_required_for_that_kind()
    {
        var definition = Parse(
            """
            {"id":"fd_kind00001","type":"select","label":"Client type","required":true,"props":{"options":[{"value":"person","label":"Person"},{"value":"sole","label":"Sole owner"},{"value":"company","label":"Company"}]}},
            {"id":"fd_first0001","type":"text","label":"First name","requiredWhen":{"field":"fd_kind00001","op":"in","value":["person","sole"]},"props":{"maxLength":80}},
            {"id":"fd_last00001","type":"text","label":"Last name","requiredWhen":{"not":{"field":"fd_kind00001","op":"equals","value":"company"}},"props":{"maxLength":80}},
            {"id":"fd_legal0001","type":"text","label":"Company name","requiredWhen":{"any":[{"field":"fd_kind00001","op":"equals","value":"company"}]},"props":{}},
            {"id":"fd_email0001","type":"email","label":"Email","required":true,"props":{}}
            """,
            """
            {"componentId":"fd_kind00001","targetKey":"customer.kind","optionMap":{"person":"Individual","sole":"Individual","company":"Business"}},
            {"componentId":"fd_first0001","targetKey":"customer.person.first_name"},
            {"componentId":"fd_last00001","targetKey":"customer.person.last_name"},
            {"componentId":"fd_legal0001","targetKey":"customer.business.legal_name"},
            {"componentId":"fd_email0001","targetKey":"customer.primary_email"}
            """,
            """{"key":"create_client","type":"createCustomer","trigger":"onSubmit","config":{"kind":{"mapped":true},"language":{"constant":"Es"}}}"""
        );

        Assert.Empty(Validate(definition));
    }

    [Fact]
    public void Field_hidden_by_an_unrelated_condition_is_not_always_answered()
    {
        var definition = Parse(
            """
            {"id":"fd_toggle001","type":"yesNo","label":"Continue?","required":true,"props":{}},
            {"id":"fd_first0001","type":"text","label":"First name","required":true,"visibleWhen":{"field":"fd_toggle001","op":"isTrue"},"props":{"maxLength":80}},
            {"id":"fd_last00001","type":"text","label":"Last name","required":true,"props":{"maxLength":80}},
            {"id":"fd_email0001","type":"email","label":"Email","required":true,"props":{}}
            """,
            IndividualMappings,
            Create
        );

        Assert.Equal([(IssueCodes.MappingSourceNotRequired, "/mappings/0/componentId")], Codes(Validate(definition)));
    }

    [Fact]
    public void Business_constant_only_requires_business_targets()
    {
        var definition = Parse(
            """
            {"id":"fd_legal0001","type":"text","label":"Company name","required":true,"props":{}},
            {"id":"fd_email0001","type":"email","label":"Email","required":true,"props":{}}
            """,
            """
            {"componentId":"fd_legal0001","targetKey":"customer.business.legal_name"},
            {"componentId":"fd_email0001","targetKey":"customer.primary_email"}
            """,
            """{"key":"create_client","type":"createCustomer","trigger":"onSubmit","config":{"kind":{"constant":"Business"},"language":{"constant":"En"}}}"""
        );

        Assert.Empty(Validate(definition));
    }

    [Fact]
    public void Missing_kind_requires_the_kind_and_every_kind_specific_target()
    {
        var definition = Parse(
            Individual,
            IndividualMappings,
            """{"key":"create_client","type":"createCustomer","trigger":"onSubmit","config":{"language":{"constant":"En"}}}"""
        );

        var issues = Validate(definition);

        Assert.All(issues, issue => Assert.Equal(IssueCodes.MappingRequiredTargetMissing, issue.Code));
        Assert.Contains(issues, issue => issue.Message.Contains("customer.kind", StringComparison.Ordinal));
        Assert.Contains(
            issues,
            issue => issue.Message.Contains("customer.business.legal_name", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Mappings_without_actions_need_no_coverage()
    {
        var definition = Parse(
            """{"id":"fd_email0001","type":"email","label":"Email","props":{}}""",
            """{"componentId":"fd_email0001","targetKey":"customer.primary_email"}""",
            null
        );

        Assert.Empty(Validate(definition));
    }

    [Fact]
    public void Admin_only_action_requires_tenant_admin()
    {
        var snapshot = MappingTestCatalog.Snapshot() with
        {
            Operations =
            [
                new(MappingTestCatalog.Create, MappingTestCatalog.Manage, true, null, new Dictionary<string, string>()),
            ],
        };
        var definition = Parse(Individual, IndividualMappings, Create);

        var staff = Validate(definition, snapshot);
        var admin = EngineTestFactory.Create().ValidateMappings(definition, snapshot, MappingTestCatalog.Admin).Issues;

        Assert.Contains((IssueCodes.MappingPermissionDenied, "/actions/0/type"), Codes(staff));
        Assert.DoesNotContain(admin, issue => issue.Code == IssueCodes.MappingPermissionDenied);
    }

    [Fact]
    public void Canonical_rule_sets_are_stable_and_empty_when_nothing_is_configured()
    {
        var engine = EngineTestFactory.Create();
        var configured = engine.CanonicalizeRuleSets(Parse(Individual, IndividualMappings, Create));
        var empty = engine.CanonicalizeRuleSets(Parse(Individual, null, null));

        Assert.True(empty.IsEmpty);
        Assert.Contains(
            "\"targetKey\":\"customer.primary_email\"",
            configured.MappingSetJson,
            StringComparison.Ordinal
        );
        Assert.Contains("\"type\":\"createCustomer\"", configured.ActionSetJson, StringComparison.Ordinal);
        Assert.Equal(configured, engine.CanonicalizeRuleSets(Parse(Individual, IndividualMappings, Create)));
    }

    [Theory]
    [InlineData(MappingTargetType.Email, MappingTransform.DigitsOnly, false)]
    [InlineData(MappingTargetType.Email, MappingTransform.Lowercase, true)]
    [InlineData(MappingTargetType.Phone, MappingTransform.Trim, true)]
    [InlineData(MappingTargetType.Enum, MappingTransform.Trim, false)]
    [InlineData(MappingTargetType.String, MappingTransform.Uppercase, true)]
    [InlineData(MappingTargetType.Date, MappingTransform.None, true)]
    public void Transforms_are_closed_per_target_type(
        MappingTargetType type,
        MappingTransform transform,
        bool allowed
    ) => Assert.Equal(allowed, MappingCompatibility.AllowsTransform(type, transform));

    private static IReadOnlyList<ValidationIssue> Validate(
        FormDefinition definition,
        MappingCatalogSnapshot? snapshot = null
    ) =>
        EngineTestFactory
            .Create()
            .ValidateMappings(definition, snapshot ?? MappingTestCatalog.Snapshot(), MappingTestCatalog.Staff)
            .Issues;

    private static (string Code, string Path)[] Codes(IReadOnlyList<ValidationIssue> issues) =>
        issues.Select(issue => (issue.Code, issue.Path)).ToArray();

    private static FormDefinition Parse(
        string components,
        string? mappings,
        string? actions,
        bool withDependents = false
    )
    {
        var dependents = withDependents
            ? """
                ,{"id":"sc_deps0001","title":"Dependents","repeat":{"minRows":0,"maxRows":5,"addLabel":"Add","rowLabel":"Dependent"},"components":[
                {"id":"fd_depfirst1","type":"text","label":"First name","required":true,"props":{"maxLength":80}},
                {"id":"fd_deplast01","type":"text","label":"Last name","required":true,"props":{"maxLength":80}},
                {"id":"fd_deprel001","type":"select","label":"Relationship","props":{"options":[{"value":"child","label":"Child"}]}}]}
                """
            : string.Empty;
        var json = $$"""
            {"schemaVersion":1,"title":"Onboarding","purpose":"clientIntake","presentation":"wizard","pages":[{"id":"pg_abcdefgh","title":"Page","sections":[{"id":"sc_abcdefgh","title":"Section","components":[{{components}}]}{{dependents}}]}],"mappings":[{{mappings}}],"actions":[{{actions}}]}
            """;
        var parsed = EngineTestFactory.Create().ParseDefinition(json);
        Assert.True(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.Message : null);
        return parsed.Value;
    }
}
