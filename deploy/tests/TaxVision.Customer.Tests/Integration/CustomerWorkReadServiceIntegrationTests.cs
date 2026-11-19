using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// Work Profile de Customer en SQL Server: clientes como preparador / asignados, filtro del listado por persona,
/// ABAC del que mira (conteos incluidos), archivados fuera y nunca otro tenant.
/// </summary>
public sealed class CustomerWorkReadServiceIntegrationTests(CustomerTemporaryDatabase database)
    : IClassFixture<CustomerTemporaryDatabase>
{
    private static readonly Guid Admin = Guid.NewGuid();

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _preparer = Guid.NewGuid();
    private readonly Guid _viewer = Guid.NewGuid();

    [Fact]
    public async Task Counts_preparer_and_assigned_without_archived_or_other_tenants()
    {
        await SeedScenarioAsync();

        var counts = await Reads().CountAsync(_tenant, _preparer, visibleTo: null, CancellationToken.None);

        // Preparador: Shared + Hidden (Archived fuera). Asignado: + Additional.
        Assert.Equal(new CustomerWorkCounts(2, 3), counts);
    }

    [Fact]
    public async Task Counts_with_abac_only_count_what_the_viewer_can_see()
    {
        await SeedScenarioAsync();

        var counts = await Reads().CountAsync(_tenant, _preparer, visibleTo: _viewer, CancellationToken.None);

        Assert.Equal(new CustomerWorkCounts(1, 1), counts);
    }

    [Fact]
    public async Task Top_is_capped_most_recent_first_and_respects_abac()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
            ids.Add(await SeedAsync(_tenant, $"Top{i}", c => c.AssignPreparer(_preparer, Admin)));
        await TouchAsync(ids[2]);

        var top = await Reads().TopAsPreparerAsync(_tenant, _preparer, null, 5, CancellationToken.None);
        var hidden = await Reads().TopAsPreparerAsync(_tenant, _preparer, _viewer, 5, CancellationToken.None);

        Assert.Equal(5, top.Count);
        Assert.Equal(ids[2], top[0].Id);
        Assert.Equal("Top2 Client", top[0].DisplayName);
        Assert.Equal(CustomerStatus.Active, top[0].Status);
        Assert.Empty(hidden);
    }

    [Fact]
    public async Task Batch_counts_preparer_clients_per_user_with_abac()
    {
        await SeedScenarioAsync();
        var nobody = Guid.NewGuid();

        var all = await Reads().CountAsPreparerAsync(_tenant, [_preparer, nobody], null, CancellationToken.None);
        var visible = await Reads().CountAsPreparerAsync(_tenant, [_preparer, nobody], _viewer, CancellationToken.None);
        var otherTenant = await Reads().CountAsPreparerAsync(Guid.NewGuid(), [_preparer], null, CancellationToken.None);

        Assert.Equal(2, all[_preparer]);
        Assert.False(all.ContainsKey(nobody));
        Assert.Equal(1, visible[_preparer]);
        Assert.Empty(otherTenant);
    }

    [Fact]
    public async Task Search_filters_by_preparer_and_by_assigned_combined_with_abac()
    {
        var names = await SeedScenarioAsync();

        var asPreparer = await SearchAsync(new CustomerStaffFilter(_preparer, null), assignedTo: null);
        var assigned = await SearchAsync(new CustomerStaffFilter(null, _preparer), assignedTo: null);
        var visible = await SearchAsync(new CustomerStaffFilter(null, _preparer), assignedTo: _viewer);
        var both = await SearchAsync(new CustomerStaffFilter(_preparer, _viewer), assignedTo: null);

        Assert.Equal([names.Hidden, names.Shared], asPreparer);
        Assert.Equal([names.Additional, names.Hidden, names.Shared], assigned);
        Assert.Equal([names.Shared], visible);
        Assert.Equal([names.Shared], both);
    }

    [Fact]
    public async Task Search_by_staff_never_returns_other_tenants()
    {
        await SeedScenarioAsync();

        var page = await new CustomerReadService(database.NewContext(Guid.Empty), new PlainProtector()).SearchAsync(
            Guid.NewGuid(),
            null,
            CustomerStatusFilter.All,
            1,
            50,
            staff: new CustomerStaffFilter(_preparer, _preparer)
        );

        Assert.Empty(page.Items);
    }

    /// <summary>Shared (preparador + visible al viewer), Hidden (preparador), Additional (acceso extra), archivado,
    /// y uno del preparador en otro tenant.</summary>
    private async Task<(string Shared, string Hidden, string Additional)> SeedScenarioAsync()
    {
        await SeedAsync(
            _tenant,
            "Shared",
            c =>
            {
                c.AssignPreparer(_preparer, Admin);
                return c.GrantAccess(_viewer, Admin);
            }
        );
        await SeedAsync(_tenant, "Hidden", c => c.AssignPreparer(_preparer, Admin));
        await SeedAsync(_tenant, "Additional", c => c.GrantAccess(_preparer, Admin));
        await SeedAsync(
            _tenant,
            "Archived",
            c =>
            {
                c.AssignPreparer(_preparer, Admin);
                c.GrantAccess(_viewer, Admin);
                return c.Archive(Admin);
            }
        );
        await SeedAsync(Guid.NewGuid(), "Foreign", c => c.AssignPreparer(_preparer, Admin));
        return ("Shared Client", "Hidden Client", "Additional Client");
    }

    private async Task<Guid> SeedAsync(
        Guid tenantId,
        string firstName,
        Func<DomainCustomer, BuildingBlocks.Results.Result> arrange
    )
    {
        var customer = DomainCustomer
            .Register(
                tenantId,
                CustomerKind.Individual,
                PersonalName.Create(firstName, "Client").Value,
                null,
                EmailAddress.Create($"work-{Guid.NewGuid():N}@example.test").Value,
                null,
                Language.En,
                Admin
            )
            .Value;
        Assert.True(arrange(customer).IsSuccess);
        await using var db = database.NewContext(tenantId);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task TouchAsync(Guid customerId)
    {
        await Task.Delay(20);
        await using var db = database.NewContext(_tenant);
        var customer = db.Customers.Single(c => c.Id == customerId);
        Assert.True(customer.ChangeLanguage(Language.Es, Admin).IsSuccess);
        await db.SaveChangesAsync();
    }

    private async Task<string[]> SearchAsync(CustomerStaffFilter staff, Guid? assignedTo)
    {
        var page = await new CustomerReadService(database.NewContext(Guid.Empty), new PlainProtector()).SearchAsync(
            _tenant,
            null,
            CustomerStatusFilter.Active,
            1,
            50,
            assignedToUserId: assignedTo,
            staff: staff
        );
        return [.. page.Items.Select(i => i.DisplayName).Order(StringComparer.Ordinal)];
    }

    private CustomerWorkReadService Reads() => new(database.NewContext(Guid.Empty));

    private sealed class PlainProtector : ISensitiveDataProtector
    {
        public byte[] Protect(string plainText) => System.Text.Encoding.UTF8.GetBytes(plainText);

        public string Unprotect(byte[] cipher) => System.Text.Encoding.UTF8.GetString(cipher);

        public string ComputeBlindIndex(string plainText, Guid tenantId) => plainText;

        public IReadOnlyCollection<string> ComputeBlindIndexCandidates(string plainText, Guid tenantId) => [plainText];

        public short CurrentKeyVersion => 1;
    }
}
