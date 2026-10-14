using TaxVision.CloudStorage.Application.CustomerAccess.Commands.SetDenyOverride;
using TaxVision.CloudStorage.Domain.CustomerAccess;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.CustomerAccess;

public sealed class SetDenyOverrideHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private static Folder CustomerFolder(Guid? ownerId = null) =>
        Folder
            .Create(
                Guid.NewGuid(),
                Tenant,
                OwnerType.Customer,
                ownerId ?? Customer,
                null,
                FolderName.Create("DocsCliente").Value,
                null,
                Guid.NewGuid(),
                Now
            )
            .Value;

    private static FileObject CustomerFile(Guid? ownerId = null)
    {
        var owner = ownerId ?? Customer;
        var key = ObjectKey
            .Create($"tenants/{Tenant:N}/customer/{owner:N}/documents/2026/{Guid.NewGuid():N}.pdf")
            .Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                Tenant,
                OwnerType.Customer,
                owner,
                FolderType.Documents,
                2026,
                key,
                "doc.pdf",
                "application/pdf",
                100,
                Guid.NewGuid(),
                Now,
                Now.AddHours(24)
            )
            .Value;
    }

    private static (
        FakeCustomerAccessRuleRepository rules,
        FakeCustomerAccessRuleAuditRepository audit,
        FakeFolderRepository folders,
        FakeFileObjectRepository files,
        FakeCustomerAssignmentProjectionStore assignments,
        FakeSystemClock clock,
        FakeUnitOfWork uow
    ) NewFixture()
    {
        return (
            new FakeCustomerAccessRuleRepository(),
            new FakeCustomerAccessRuleAuditRepository(),
            new FakeFolderRepository(),
            new FakeFileObjectRepository(),
            new FakeCustomerAssignmentProjectionStore(),
            new FakeSystemClock(Now),
            new FakeUnitOfWork()
        );
    }

    [Fact]
    public async Task Staff_without_any_permission_is_forbidden()
    {
        var fx = NewFixture();
        var file = CustomerFile();
        fx.files.Seed(file);

        var result = await SetDenyOverrideHandler.Handle(
            new SetDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.File,
                file.Id,
                AccessCapability.Download,
                null,
                HasManageAny: false,
                HasManageAssigned: false
            ),
            fx.rules,
            fx.audit,
            fx.folders,
            fx.files,
            fx.assignments,
            fx.clock,
            fx.uow,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(CustomerAccessErrors.Forbidden, result.Error);
        Assert.Empty(fx.rules.All());
        Assert.Empty(fx.audit.Entries);
    }

    [Fact]
    public async Task Staff_with_manage_assigned_without_assignment_is_forbidden()
    {
        var fx = NewFixture();
        var file = CustomerFile();
        fx.files.Seed(file);

        var result = await SetDenyOverrideHandler.Handle(
            new SetDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.File,
                file.Id,
                AccessCapability.Download,
                null,
                HasManageAny: false,
                HasManageAssigned: true
            ),
            fx.rules,
            fx.audit,
            fx.folders,
            fx.files,
            fx.assignments,
            fx.clock,
            fx.uow,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(CustomerAccessErrors.Forbidden, result.Error);
    }

    [Fact]
    public async Task Staff_with_manage_assigned_and_assignment_can_create_rule()
    {
        var fx = NewFixture();
        var file = CustomerFile();
        fx.files.Seed(file);
        fx.assignments.Assign(Tenant, Customer, Actor);

        var result = await SetDenyOverrideHandler.Handle(
            new SetDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.File,
                file.Id,
                AccessCapability.Download,
                "sensitive",
                HasManageAny: false,
                HasManageAssigned: true
            ),
            fx.rules,
            fx.audit,
            fx.folders,
            fx.files,
            fx.assignments,
            fx.clock,
            fx.uow,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);
        var rule = Assert.Single(fx.rules.All());
        Assert.Equal(AccessCapability.Download, rule.Capability);
        Assert.Equal("sensitive", rule.Reason);
        Assert.Single(fx.audit.Entries);
        Assert.Equal(AccessRuleAction.Created, fx.audit.Entries[0].Action);
        Assert.Equal(1, fx.uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task TenantAdmin_with_manage_any_can_create_rule_without_assignment()
    {
        var fx = NewFixture();
        var file = CustomerFile();
        fx.files.Seed(file);

        var result = await SetDenyOverrideHandler.Handle(
            new SetDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.File,
                file.Id,
                AccessCapability.Delete,
                null,
                HasManageAny: true,
                HasManageAssigned: false
            ),
            fx.rules,
            fx.audit,
            fx.folders,
            fx.files,
            fx.assignments,
            fx.clock,
            fx.uow,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(fx.rules.All());
    }

    [Fact]
    public async Task File_not_found_returns_resource_not_found()
    {
        var fx = NewFixture();
        var result = await SetDenyOverrideHandler.Handle(
            new SetDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.File,
                Guid.NewGuid(),
                AccessCapability.Preview,
                null,
                HasManageAny: true,
                HasManageAssigned: false
            ),
            fx.rules,
            fx.audit,
            fx.folders,
            fx.files,
            fx.assignments,
            fx.clock,
            fx.uow,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(CustomerAccessErrors.ResourceNotFound, result.Error);
    }

    [Fact]
    public async Task File_owned_by_a_different_customer_rejects()
    {
        var fx = NewFixture();
        var otherCustomer = Guid.NewGuid();
        var file = CustomerFile(otherCustomer);
        fx.files.Seed(file);

        var result = await SetDenyOverrideHandler.Handle(
            new SetDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.File,
                file.Id,
                AccessCapability.Preview,
                null,
                HasManageAny: true,
                HasManageAssigned: false
            ),
            fx.rules,
            fx.audit,
            fx.folders,
            fx.files,
            fx.assignments,
            fx.clock,
            fx.uow,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(CustomerAccessErrors.ResourceNotOwnedByCustomer, result.Error);
    }

    [Fact]
    public async Task Folder_resource_is_validated_against_owner()
    {
        var fx = NewFixture();
        var folder = CustomerFolder();
        fx.folders.Seed(folder);

        var result = await SetDenyOverrideHandler.Handle(
            new SetDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.Folder,
                folder.Id,
                AccessCapability.Visible,
                null,
                HasManageAny: true,
                HasManageAssigned: false
            ),
            fx.rules,
            fx.audit,
            fx.folders,
            fx.files,
            fx.assignments,
            fx.clock,
            fx.uow,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(fx.rules.All());
    }

    [Fact]
    public async Task TenantScope_with_matching_tenant_id_creates_preset_rule()
    {
        // F7: preset tenant-wide. Sin folder/file que validar; el ResourceId debe ser el TenantId.
        var fx = NewFixture();

        var result = await SetDenyOverrideHandler.Handle(
            new SetDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.TenantScope,
                Tenant,
                AccessCapability.Download,
                "Read-only preset",
                HasManageAny: true,
                HasManageAssigned: false
            ),
            fx.rules,
            fx.audit,
            fx.folders,
            fx.files,
            fx.assignments,
            fx.clock,
            fx.uow,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var rule = Assert.Single(fx.rules.All());
        Assert.Equal(AccessResourceType.TenantScope, rule.ResourceType);
        Assert.Equal(Tenant, rule.ResourceId);
        Assert.Equal(AccessCapability.Download, rule.Capability);
    }

    [Fact]
    public async Task TenantScope_with_mismatched_resource_id_is_rejected()
    {
        // Fail-closed: aunque el body traiga un TenantScope, el ResourceId tiene que coincidir con el TenantId del JWT.
        var fx = NewFixture();

        var result = await SetDenyOverrideHandler.Handle(
            new SetDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.TenantScope,
                Guid.NewGuid(),
                AccessCapability.Download,
                null,
                HasManageAny: true,
                HasManageAssigned: false
            ),
            fx.rules,
            fx.audit,
            fx.folders,
            fx.files,
            fx.assignments,
            fx.clock,
            fx.uow,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(CustomerAccessErrors.ResourceNotOwnedByCustomer, result.Error);
        Assert.Empty(fx.rules.All());
    }

    [Fact]
    public async Task Second_set_updates_reason_and_logs_audit()
    {
        var fx = NewFixture();
        var file = CustomerFile();
        fx.files.Seed(file);

        var first = await SetDenyOverrideHandler.Handle(
            new SetDenyOverrideCommand(
                Tenant,
                Actor,
                Customer,
                AccessResourceType.File,
                file.Id,
                AccessCapability.Download,
                "first",
                HasManageAny: true,
                HasManageAssigned: false
            ),
            fx.rules,
            fx.audit,
            fx.folders,
            fx.files,
            fx.assignments,
            fx.clock,
            fx.uow,
            CancellationToken.None
        );
        Assert.True(first.IsSuccess);
        Assert.True(first.Value.Created);

        var newActor = Guid.NewGuid();
        var later = new FakeSystemClock(Now.AddHours(1));
        var second = await SetDenyOverrideHandler.Handle(
            new SetDenyOverrideCommand(
                Tenant,
                newActor,
                Customer,
                AccessResourceType.File,
                file.Id,
                AccessCapability.Download,
                "second",
                HasManageAny: true,
                HasManageAssigned: false
            ),
            fx.rules,
            fx.audit,
            fx.folders,
            fx.files,
            fx.assignments,
            later,
            fx.uow,
            CancellationToken.None
        );

        Assert.True(second.IsSuccess);
        Assert.False(second.Value.Created);
        var only = Assert.Single(fx.rules.All());
        Assert.Equal(newActor, only.SetByUserId);
        Assert.Equal("second", only.Reason);
        Assert.Equal(2, fx.audit.Entries.Count);
        Assert.All(fx.audit.Entries, e => Assert.Equal(AccessRuleAction.Created, e.Action));
    }
}
