using TaxVision.CloudStorage.Application.CustomerAccess.Commands.ClearDenyOverride;
using TaxVision.CloudStorage.Domain.CustomerAccess;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.CustomerAccess;

public sealed class ClearDenyOverrideHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Resource = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private static CustomerDocumentAccessRule SeededRule(FakeCustomerAccessRuleRepository rules)
    {
        var rule = CustomerDocumentAccessRule
            .Create(
                Guid.NewGuid(),
                Tenant,
                Customer,
                AccessResourceType.File,
                Resource,
                AccessCapability.Download,
                Actor,
                Now.AddMinutes(-5),
                "already denied"
            )
            .Value;
        rules.Add(rule);
        return rule;
    }

    [Fact]
    public async Task Clear_without_permission_is_forbidden()
    {
        var rules = new FakeCustomerAccessRuleRepository();
        SeededRule(rules);
        var audit = new FakeCustomerAccessRuleAuditRepository();
        var assignments = new FakeCustomerAssignmentProjectionStore();

        var result = await ClearDenyOverrideHandler.Handle(
            new ClearDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.File,
                Resource,
                AccessCapability.Download,
                HasManageAny: false,
                HasManageAssigned: false
            ),
            rules,
            audit,
            assignments,
            new FakeSystemClock(Now),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Single(rules.All()); // sin cambios
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Clear_existing_rule_removes_it_and_logs_removed_audit()
    {
        var rules = new FakeCustomerAccessRuleRepository();
        SeededRule(rules);
        var audit = new FakeCustomerAccessRuleAuditRepository();
        var uow = new FakeUnitOfWork();

        var result = await ClearDenyOverrideHandler.Handle(
            new ClearDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.File,
                Resource,
                AccessCapability.Download,
                HasManageAny: true,
                HasManageAssigned: false
            ),
            rules,
            audit,
            new FakeCustomerAssignmentProjectionStore(),
            new FakeSystemClock(Now),
            uow,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Removed);
        Assert.Empty(rules.All());
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AccessRuleAction.Removed, entry.Action);
        Assert.Equal("already denied", entry.Reason);
        Assert.Equal(1, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Clear_nonexistent_rule_is_idempotent_without_audit()
    {
        var rules = new FakeCustomerAccessRuleRepository();
        var audit = new FakeCustomerAccessRuleAuditRepository();
        var uow = new FakeUnitOfWork();

        var result = await ClearDenyOverrideHandler.Handle(
            new ClearDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.File,
                Resource,
                AccessCapability.Download,
                HasManageAny: true,
                HasManageAssigned: false
            ),
            rules,
            audit,
            new FakeCustomerAssignmentProjectionStore(),
            new FakeSystemClock(Now),
            uow,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Removed);
        Assert.Empty(audit.Entries);
        Assert.Equal(0, uow.SaveChangesCallCount);
    }
}
