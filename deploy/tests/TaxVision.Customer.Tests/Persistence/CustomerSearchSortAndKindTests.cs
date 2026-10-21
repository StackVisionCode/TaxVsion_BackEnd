using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Persistence;

/// <summary>
/// Orden (nombre/fecha, asc/desc) y filtro por tipo del listado de Clients. El orden tiene que ser
/// estable entre páginas: recorrer todas las páginas trae cada cliente exactamente una vez.
/// </summary>
public sealed class CustomerSearchSortAndKindTests
{
    private static readonly Guid ByUser = Guid.NewGuid();
    private static readonly DateTime BaseDate = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private sealed class NoopProtector : ISensitiveDataProtector
    {
        public byte[] Protect(string plainText) => System.Text.Encoding.UTF8.GetBytes(plainText);

        public string Unprotect(byte[] cipher) => System.Text.Encoding.UTF8.GetString(cipher);

        public string ComputeBlindIndex(string plainText, Guid tenantId) => plainText;
    }

    private static CustomerDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(new DbContextOptionsBuilder<CustomerDbContext>().UseInMemoryDatabase(databaseName).Options, tenantContext);

    private static DomainCustomer Individual(Guid tenantId, string first, string last) =>
        DomainCustomer
            .Register(
                tenantId,
                CustomerKind.Individual,
                PersonalName.Create(first, last).Value,
                null,
                EmailAddress.Create($"{first}-{Guid.NewGuid():N}@example.com").Value,
                null,
                Language.En,
                ByUser
            )
            .Value;

    private static DomainCustomer Business(Guid tenantId, string legalName) =>
        DomainCustomer
            .Register(
                tenantId,
                CustomerKind.Business,
                null,
                BusinessIdentity.Create(legalName, BusinessStructure.Llc).Value,
                EmailAddress.Create($"biz-{Guid.NewGuid():N}@example.com").Value,
                null,
                Language.En,
                ByUser
            )
            .Value;

    /// <summary>Siembra los clientes con fechas de alta conocidas (día N = índice N, o todas iguales).</summary>
    private static async Task<(CustomerReadService Reader, Guid TenantId)> SeedAsync(
        Func<Guid, IReadOnlyList<DomainCustomer>> build,
        bool sameDate = false
    )
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var customers = build(tenantId);
            for (var i = 0; i < customers.Count; i++)
            {
                seedDb.Customers.Add(customers[i]);
                seedDb.Entry(customers[i]).Property(c => c.CreatedAtUtc).CurrentValue = sameDate
                    ? BaseDate
                    : BaseDate.AddDays(i);
            }
            await seedDb.SaveChangesAsync();
        }

        return (new CustomerReadService(CreateContext(databaseName, tenantContext), new NoopProtector()), tenantId);
    }

    private static IReadOnlyList<DomainCustomer> Mixed(Guid tenantId) =>
        [
            Individual(tenantId, "Carla", "Diaz"), // día 0
            Business(tenantId, "Anchor Builders"), // día 1
            Individual(tenantId, "Ana", "Torres"), // día 2
            Business(tenantId, "Zenith Capital"), // día 3
        ];

    [Fact]
    public async Task By_default_it_sorts_by_name_a_to_z()
    {
        var (reader, tenantId) = await SeedAsync(Mixed);

        var page = await reader.SearchAsync(tenantId, null, CustomerStatusFilter.All, 1, 10);

        Assert.Equal(
            ["Ana Torres", "Anchor Builders", "Carla Diaz", "Zenith Capital"],
            page.Items.Select(c => c.DisplayName)
        );
    }

    [Fact]
    public async Task Name_descending_goes_z_to_a()
    {
        var (reader, tenantId) = await SeedAsync(Mixed);

        var page = await reader.SearchAsync(
            tenantId,
            null,
            CustomerStatusFilter.All,
            1,
            10,
            sort: CustomerSortField.Name,
            direction: CustomerSortDirection.Desc
        );

        Assert.Equal(
            ["Zenith Capital", "Carla Diaz", "Anchor Builders", "Ana Torres"],
            page.Items.Select(c => c.DisplayName)
        );
    }

    [Theory]
    [InlineData(CustomerSortDirection.Desc, new[] { "Zenith Capital", "Ana Torres", "Anchor Builders", "Carla Diaz" })]
    [InlineData(CustomerSortDirection.Asc, new[] { "Carla Diaz", "Anchor Builders", "Ana Torres", "Zenith Capital" })]
    public async Task Created_sorts_by_signup_date(CustomerSortDirection direction, string[] expected)
    {
        var (reader, tenantId) = await SeedAsync(Mixed);

        var page = await reader.SearchAsync(
            tenantId,
            null,
            CustomerStatusFilter.All,
            1,
            10,
            sort: CustomerSortField.Created,
            direction: direction
        );

        Assert.Equal(expected, page.Items.Select(c => c.DisplayName));
    }

    [Theory]
    [InlineData(CustomerKind.Individual, new[] { "Ana Torres", "Carla Diaz" })]
    [InlineData(CustomerKind.Business, new[] { "Anchor Builders", "Zenith Capital" })]
    public async Task The_type_filter_keeps_only_that_kind_and_counts_only_them(CustomerKind kind, string[] expected)
    {
        var (reader, tenantId) = await SeedAsync(Mixed);

        var page = await reader.SearchAsync(tenantId, null, CustomerStatusFilter.All, 1, 10, kind: kind);

        Assert.Equal(expected, page.Items.Select(c => c.DisplayName));
        Assert.Equal(2, page.TotalCount);
    }

    [Theory]
    [InlineData(CustomerSortField.Name, CustomerSortDirection.Asc)]
    [InlineData(CustomerSortField.Name, CustomerSortDirection.Desc)]
    [InlineData(CustomerSortField.Created, CustomerSortDirection.Desc)]
    public async Task Walking_every_page_returns_each_client_once_even_with_repeated_names_and_dates(
        CustomerSortField sort,
        CustomerSortDirection direction
    )
    {
        // 7 clientes con el MISMO nombre y la MISMA fecha: sin desempate, las páginas podían repetir o saltar filas.
        var (reader, tenantId) = await SeedAsync(
            tenantId => Enumerable.Range(0, 7).Select(_ => Individual(tenantId, "Ana", "Torres")).ToList(),
            sameDate: true
        );
        var seen = new List<Guid>();

        for (var page = 1; page <= 3; page++)
        {
            var result = await reader.SearchAsync(
                tenantId,
                null,
                CustomerStatusFilter.All,
                page,
                3,
                sort: sort,
                direction: direction
            );
            seen.AddRange(result.Items.Select(c => c.Id));
        }

        Assert.Equal(7, seen.Count);
        Assert.Equal(7, seen.Distinct().Count());
    }
}
