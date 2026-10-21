using BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class CustomerDataCollectionTargetsTests
{
    [Fact]
    public void V1_has_unique_stable_targets_and_excludes_unsynchronized_catalog_ids()
    {
        Assert.Equal(1, CustomerDataCollectionTargets.CatalogVersion);
        Assert.Equal(21, CustomerDataCollectionTargets.All.Count);
        Assert.Equal(
            CustomerDataCollectionTargets.All.Count,
            CustomerDataCollectionTargets.All.Select(target => target.TargetKey).Distinct(StringComparer.Ordinal).Count()
        );
        Assert.DoesNotContain(CustomerDataCollectionTargets.All, target => target.TargetKey.Contains("occupation", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(CustomerDataCollectionTargets.All, target => target.TargetKey.Contains("business_activity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Restricted_and_update_only_targets_expose_their_contract()
    {
        var taxId = Find("customer.fiscal.tax_identifier");
        var dependents = Find("customer.relation.dependents");

        Assert.Equal(DataCollectionSensitivity.Restricted, taxId.Sensitivity);
        Assert.Equal(DataCollectionTargetType.TaxId, taxId.DataType);
        Assert.True(taxId.RequiresTenantAdmin);
        Assert.Equal([DataCollectionOperation.UpdateCustomerProfile], taxId.Operations);
        Assert.Equal(["customers.manage"], taxId.RequiredPermissions);
        Assert.Equal([DataCollectionOperation.UpdateCustomerProfile], dependents.Operations);
    }

    [Fact]
    public void Create_requirements_are_explicit_by_customer_kind()
    {
        Assert.Contains(Find("customer.kind").RequiredFor, requirement => requirement.Operation == DataCollectionOperation.CreateCustomer && requirement.Kind is null);
        Assert.Contains(Find("customer.person.first_name").RequiredFor, requirement => requirement.Kind == "Individual");
        Assert.Contains(Find("customer.business.legal_name").RequiredFor, requirement => requirement.Kind == "Business");
        Assert.Contains(Find("customer.primary_email").RequiredFor, requirement => requirement.Operation == DataCollectionOperation.CreateCustomer);
    }

    private static DataCollectionTarget Find(string key) =>
        Assert.Single(CustomerDataCollectionTargets.All, target => target.TargetKey == key);
}
