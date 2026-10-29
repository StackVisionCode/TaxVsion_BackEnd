using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;
using TaxVision.Customer.Application.Customers.FromForms;
using TaxVision.Customer.Domain.Customers;
using Outcomes = BuildingBlocks.Messaging.ClientFormsIntegrationEvents.CustomerCreationFromFormOutcomes;

namespace TaxVision.Customer.Tests.Application;

public sealed class CustomerFormTargetMapperTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Staff = Guid.NewGuid();

    [Fact]
    public void The_create_command_never_overwrites_an_existing_customer()
    {
        var draft = CustomerFormTargetMapper.Map(CustomerFormCreationHarness.IndividualValues()).Value;

        var command = draft.ToCommand(Tenant, Staff);

        Assert.False(command.Overwrite);
        Assert.Equal(Staff, command.CreatedByUserId);
        Assert.Equal(CustomerKind.Individual, command.Kind);
        Assert.Equal(Language.En, command.Language);
        Assert.Null(command.OccupationId);
        Assert.Null(command.PrincipalBusinessActivityId);
    }

    [Fact]
    public void Maps_a_business_with_its_required_targets()
    {
        List<ClientFormTargetValue> values =
        [
            new("customer.kind", 1, "Business"),
            new("customer.business.legal_name", 1, "Analytical Engines LLC"),
            new("customer.business.structure", 1, "Llc"),
            new("customer.business.formation_date", 1, "2001-03-04"),
            new("customer.primary_email", 1, "office@engines.example"),
            new("customer.language", 1, "Es"),
        ];

        var draft = CustomerFormTargetMapper.Map(values);

        Assert.True(draft.IsSuccess);
        Assert.Equal(BusinessStructure.Llc, draft.Value.BusinessStructure);
        Assert.Equal(new DateOnly(2001, 3, 4), draft.Value.FormationDate);
    }

    [Theory]
    [InlineData("customer.kind")]
    [InlineData("customer.language")]
    [InlineData("customer.primary_email")]
    [InlineData("customer.person.first_name")]
    public void A_missing_required_target_is_invalid(string missing)
    {
        var values = CustomerFormCreationHarness.IndividualValues().FindAll(v => v.TargetKey != missing);

        AssertFails(values, Outcomes.FormValueInvalid);
    }

    [Theory]
    [InlineData("customer.language", "English")]
    [InlineData("customer.date_of_birth", "10/12/1985")]
    [InlineData("customer.primary_phone", "8095551234")]
    [InlineData("customer.contact.sms_enabled", "no")]
    public void A_value_outside_the_target_contract_is_invalid(string targetKey, string value)
    {
        var values = Replace(CustomerFormCreationHarness.IndividualValues(), new(targetKey, 1, value));

        AssertFails(values, Outcomes.FormValueInvalid);
    }

    [Fact]
    public void A_value_longer_than_the_catalog_limit_is_invalid()
    {
        var values = CustomerFormCreationHarness.IndividualValues();
        values.Add(new("customer.person.suffix", 1, new string('x', 21)));

        AssertFails(values, Outcomes.FormValueInvalid);
    }

    [Fact]
    public void The_same_target_twice_is_invalid()
    {
        var values = CustomerFormCreationHarness.IndividualValues();
        values.Add(new("customer.person.first_name", 1, "Augusta"));

        AssertFails(values, Outcomes.FormValueInvalid);
    }

    [Theory]
    [InlineData("customer.relation.dependents")]
    [InlineData("customer.unknown")]
    public void A_target_outside_the_create_operation_is_not_supported(string targetKey)
    {
        var values = CustomerFormCreationHarness.IndividualValues();
        values.Add(new(targetKey, 1, "x"));

        AssertFails(values, Outcomes.FormTargetNotSupported);
    }

    [Fact]
    public void A_schema_version_newer_than_the_catalog_is_not_supported()
    {
        var values = Replace(CustomerFormCreationHarness.IndividualValues(), new("customer.language", 2, "En"));

        AssertFails(values, Outcomes.FormTargetNotSupported);
    }

    [Fact]
    public void An_old_schema_value_is_translated_only_by_the_declared_legacy_map()
    {
        var catalog = WithLanguageSchemaV2(new Dictionary<string, string> { ["Spanish"] = "Es" });

        var translated = CustomerFormTargetMapper.Map(
            Replace(CustomerFormCreationHarness.IndividualValues(), new("customer.language", 1, "Spanish")),
            catalog
        );
        var undeclared = CustomerFormTargetMapper.Map(
            Replace(CustomerFormCreationHarness.IndividualValues(), new("customer.language", 1, "Portuguese")),
            catalog
        );

        Assert.Equal(Language.Es, translated.Value.Language);
        Assert.Equal(Outcomes.FormValueInvalid, undeclared.Error.Code);
    }

    [Fact]
    public void A_retired_target_is_not_executed_and_a_deprecated_one_still_is()
    {
        var retired = CustomerDataCollectionTargets
            .All.Select(t =>
                t.TargetKey == "customer.primary_phone" ? t with { Status = DataCollectionTargetStatus.Retired } : t
            )
            .ToList();
        var deprecated = CustomerDataCollectionTargets
            .All.Select(t =>
                t.TargetKey == "customer.primary_phone" ? t with { Status = DataCollectionTargetStatus.Deprecated } : t
            )
            .ToList();

        Assert.Equal(
            Outcomes.FormTargetNotSupported,
            CustomerFormTargetMapper.Map(CustomerFormCreationHarness.IndividualValues(), retired).Error.Code
        );
        Assert.True(CustomerFormTargetMapper.Map(CustomerFormCreationHarness.IndividualValues(), deprecated).IsSuccess);
    }

    private static List<DataCollectionTarget> WithLanguageSchemaV2(IReadOnlyDictionary<string, string> map) =>
        CustomerDataCollectionTargets
            .All.Select(t =>
                t.TargetKey == "customer.language"
                    ? t with
                    {
                        TargetSchemaVersion = 2,
                        LegacyValueMaps = [new DataCollectionLegacyValueMap(1, map)],
                    }
                    : t
            )
            .ToList();

    private static List<ClientFormTargetValue> Replace(List<ClientFormTargetValue> values, ClientFormTargetValue value)
    {
        values.RemoveAll(v => v.TargetKey == value.TargetKey);
        values.Add(value);
        return values;
    }

    private static void AssertFails(IReadOnlyList<ClientFormTargetValue> values, string code)
    {
        var result = CustomerFormTargetMapper.Map(values);
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
    }
}
