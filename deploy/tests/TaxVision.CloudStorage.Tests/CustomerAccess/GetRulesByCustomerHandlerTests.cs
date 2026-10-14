using TaxVision.CloudStorage.Application.CustomerAccess.Queries.GetRulesByCustomer;
using TaxVision.CloudStorage.Domain.CustomerAccess;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.CustomerAccess;

public sealed class GetRulesByCustomerHandlerTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid OtherCustomer = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CustomerDocumentAccessRule Rule(
        Guid tenant,
        Guid customer,
        AccessCapability capability,
        AccessResourceType resourceType = AccessResourceType.File,
        Guid? resourceId = null
    ) =>
        CustomerDocumentAccessRule
            .Create(
                Guid.NewGuid(),
                tenant,
                customer,
                resourceType,
                resourceId ?? Guid.NewGuid(),
                capability,
                Actor,
                Now,
                null
            )
            .Value;

    private static Folder MakeFolder(Guid tenant, Guid id, string name) =>
        Folder
            .Create(
                id,
                tenant,
                OwnerType.Customer,
                Customer,
                null,
                FolderName.Create(name).Value,
                null,
                Guid.NewGuid(),
                Now
            )
            .Value;

    private static FileObject MakeFile(Guid tenant, Guid id, string originalName)
    {
        var key = ObjectKey.Create($"tenants/{tenant:N}/customer/{Customer:N}/documents/2026/{id:N}.pdf").Value;
        return FileObject
            .Register(
                id,
                tenant,
                OwnerType.Customer,
                Customer,
                FolderType.Documents,
                2026,
                key,
                originalName,
                "application/pdf",
                100,
                Guid.NewGuid(),
                Now,
                Now.AddHours(24)
            )
            .Value;
    }

    [Fact]
    public async Task Without_permission_is_forbidden()
    {
        var rules = new FakeCustomerAccessRuleRepository();
        var folders = new FakeFolderRepository();
        var files = new FakeFileObjectRepository();
        var assignments = new FakeCustomerAssignmentProjectionStore();

        var result = await GetRulesByCustomerHandler.Handle(
            new GetRulesByCustomerQuery(TenantA, Actor, Customer, HasManageAny: false, HasManageAssigned: false),
            rules,
            folders,
            files,
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
            new FakeFolderRepository(),
            new FakeFileObjectRepository(),
            new FakeCustomerAssignmentProjectionStore(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, v => Assert.Equal(Customer, v.CustomerId));
    }

    [Fact]
    public async Task Hydrates_folder_name_when_the_folder_exists()
    {
        var folderId = Guid.NewGuid();
        var folders = new FakeFolderRepository();
        folders.Seed(MakeFolder(TenantA, folderId, "Tax Year 2025"));

        var rules = new FakeCustomerAccessRuleRepository();
        rules.Add(Rule(TenantA, Customer, AccessCapability.Download, AccessResourceType.Folder, folderId));

        var result = await GetRulesByCustomerHandler.Handle(
            new GetRulesByCustomerQuery(TenantA, Actor, Customer, HasManageAny: true, HasManageAssigned: false),
            rules,
            folders,
            new FakeFileObjectRepository(),
            new FakeCustomerAssignmentProjectionStore(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var view = Assert.Single(result.Value);
        Assert.Equal("Tax Year 2025", view.ResourceName);
    }

    [Fact]
    public async Task Hydrates_file_name_when_the_file_exists()
    {
        var fileId = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        files.Seed(MakeFile(TenantA, fileId, "return.pdf"));

        var rules = new FakeCustomerAccessRuleRepository();
        rules.Add(Rule(TenantA, Customer, AccessCapability.Preview, AccessResourceType.File, fileId));

        var result = await GetRulesByCustomerHandler.Handle(
            new GetRulesByCustomerQuery(TenantA, Actor, Customer, HasManageAny: true, HasManageAssigned: false),
            rules,
            new FakeFolderRepository(),
            files,
            new FakeCustomerAssignmentProjectionStore(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var view = Assert.Single(result.Value);
        Assert.Equal("return.pdf", view.ResourceName);
    }

    [Fact]
    public async Task Rule_over_deleted_folder_returns_null_name()
    {
        var rules = new FakeCustomerAccessRuleRepository();
        rules.Add(Rule(TenantA, Customer, AccessCapability.Download, AccessResourceType.Folder, Guid.NewGuid()));

        var result = await GetRulesByCustomerHandler.Handle(
            new GetRulesByCustomerQuery(TenantA, Actor, Customer, HasManageAny: true, HasManageAssigned: false),
            rules,
            new FakeFolderRepository(),
            new FakeFileObjectRepository(),
            new FakeCustomerAssignmentProjectionStore(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var view = Assert.Single(result.Value);
        Assert.Null(view.ResourceName);
    }

    [Fact]
    public async Task TenantScope_rule_never_has_a_resource_name()
    {
        var rules = new FakeCustomerAccessRuleRepository();
        rules.Add(Rule(TenantA, Customer, AccessCapability.Download, AccessResourceType.TenantScope, TenantA));

        var result = await GetRulesByCustomerHandler.Handle(
            new GetRulesByCustomerQuery(TenantA, Actor, Customer, HasManageAny: true, HasManageAssigned: false),
            rules,
            new FakeFolderRepository(),
            new FakeFileObjectRepository(),
            new FakeCustomerAssignmentProjectionStore(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var view = Assert.Single(result.Value);
        Assert.Equal(AccessResourceType.TenantScope, view.ResourceType);
        Assert.Null(view.ResourceName);
    }

    [Fact]
    public async Task Mix_of_existing_deleted_and_tenant_scope_resolves_each_independently()
    {
        var folderId = Guid.NewGuid();
        var deletedFileId = Guid.NewGuid();
        var folders = new FakeFolderRepository();
        folders.Seed(MakeFolder(TenantA, folderId, "Payroll"));
        var files = new FakeFileObjectRepository();
        // Nothing seeded for deletedFileId ⇒ orphan.

        var rules = new FakeCustomerAccessRuleRepository();
        rules.Add(Rule(TenantA, Customer, AccessCapability.Download, AccessResourceType.Folder, folderId));
        rules.Add(Rule(TenantA, Customer, AccessCapability.Preview, AccessResourceType.File, deletedFileId));
        rules.Add(Rule(TenantA, Customer, AccessCapability.Upload, AccessResourceType.TenantScope, TenantA));

        var result = await GetRulesByCustomerHandler.Handle(
            new GetRulesByCustomerQuery(TenantA, Actor, Customer, HasManageAny: true, HasManageAssigned: false),
            rules,
            folders,
            files,
            new FakeCustomerAssignmentProjectionStore(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        var byType = result.Value.ToLookup(v => v.ResourceType);
        Assert.Equal("Payroll", byType[AccessResourceType.Folder].Single().ResourceName);
        Assert.Null(byType[AccessResourceType.File].Single().ResourceName);
        Assert.Null(byType[AccessResourceType.TenantScope].Single().ResourceName);
    }
}
