using TaxVision.CloudStorage.Application.CustomerAccess.Queries.GetRulesByCustomer;
using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Tests.CustomerAccess;

public sealed class GetRulesByCustomerHandlerTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid OtherCustomer = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    private static CustomerDocumentAccessRule Rule(Guid tenant, Guid customer, AccessCapability capability) =>
        CustomerDocumentAccessRule
            .Create(
                Guid.NewGuid(),
                tenant,
                customer,
                AccessResourceType.File,
                Guid.NewGuid(),
                capability,
                Actor,
                Now,
                null
            )
            .Value;

    [Fact]
    public async Task Without_permission_is_forbidden()
    {
        var rules = new FakeCustomerAccessRuleRepository();
        var assignments = new FakeCustomerAssignmentProjectionStore();

        var result = await GetRulesByCustomerHandler.Handle(
            new GetRulesByCustomerQuery(TenantA, Actor, Customer, HasManageAny: false, HasManageAssigned: false),
            rules,
            assignments,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(CustomerAccessErrors.Forbidden, result.Error);
    }

    [Fact]
    public async Task Returns_only_rules_for_the_requested_tenant_and_customer()
    {
        var rules = new FakeCustomerAccessRuleRepository();
        rules.Add(Rule(TenantA, Customer, AccessCapability.Download));
        rules.Add(Rule(TenantA, Customer, AccessCapability.Visible));
        rules.Add(Rule(TenantA, OtherCustomer, AccessCapability.Download));
        rules.Add(Rule(TenantB, Customer, AccessCapability.Download));

        var result = await GetRulesByCustomerHandler.Handle(
            new GetRulesByCustomerQuery(TenantA, Actor, Customer, HasManageAny: true, HasManageAssigned: false),
            rules,
            new FakeCustomerAssignmentProjectionStore(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, v => Assert.Equal(Customer, v.CustomerId));
    }
}
