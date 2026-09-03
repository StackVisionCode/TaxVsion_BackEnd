using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Catalogs;
using TaxVision.Customer.Domain.ContactPoints;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Relations;
using TaxVision.Customer.Infrastructure.Persistence;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Persistence;

/// <summary>
/// Cubre el read model de detalle (GET /customers/{id} → CustomerDetailResponse): las sub-colecciones
/// que antes eran write-only ahora se leen de vuelta, con el DisplayName compuesto de la relación y el
/// perfil fiscal SIEMPRE enmascarado (last4, nunca el identificador completo).
/// </summary>
public sealed class CustomerDetailReadServiceTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    // GetDetailByIdAsync no usa el protector; solo hace falta para construir el read service.
    private sealed class NoopProtector : ISensitiveDataProtector
    {
        public byte[] Protect(string plainText) => System.Text.Encoding.UTF8.GetBytes(plainText);

        public string Unprotect(byte[] cipher) => System.Text.Encoding.UTF8.GetString(cipher);

        public string ComputeBlindIndex(string plainText, Guid tenantId) => plainText;
    }

    private static CustomerDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(new DbContextOptionsBuilder<CustomerDbContext>().UseInMemoryDatabase(databaseName).Options, tenantContext);

    private static DomainCustomer NewCustomer(Guid tenantId, Guid byUser)
    {
        var name = PersonalName.Create("Grace", "Hopper").Value;
        var email = EmailAddress.Create($"grace-{Guid.NewGuid():N}@example.com").Value;
        return DomainCustomer
            .Register(
                tenantId,
                CustomerKind.Individual,
                name,
                null,
                email,
                null,
                Language.En,
                PreferredChannel.Email,
                byUser
            )
            .Value;
    }

    [Fact]
    public async Task GetDetailByIdAsync_returns_scalars_collections_and_masked_fiscal()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var byUser = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        Guid customerId;
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var customer = NewCustomer(tenantId, byUser);

            var homeAddress = AddressValue.Create("123 Main St", "Miami", "33101", "US", region: "FL").Value;
            customer.AddAddress(AddressKind.Home, homeAddress, isPrimary: true, byUser);

            customer.AddContactPoint(
                ContactPointType.Phone,
                "+13055551234",
                "+13055551234",
                label: "cell",
                isPrimary: true,
                byUser
            );

            var relationName = PersonalName.Create("Ada", "Lovelace", middleName: "Byron").Value;
            customer.AddRelation(
                RelationshipKind.Spouse,
                RelationPurpose.TaxHouseholdMember,
                relationName,
                email: null,
                phone: null,
                dateOfBirth: new DateOnly(1990, 5, 1),
                address: null,
                byUser
            );

            customer.SetFiscalProfile(
                FiscalSubjectKind.Individual,
                taxIdentifierCipher: [1, 2, 3],
                taxIdentifierBlindIndex: "blind-index",
                taxIdentifierLast4: "6789",
                filingStatus: FilingStatus.Single,
                priorYearAgi: 42000m,
                isReturningCustomer: true,
                refundBankAccountCipher: [9, 9],
                refundBankRoutingCipher: null,
                byUser
            );

            await seedDb.Customers.AddAsync(customer);
            await seedDb.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using var db = CreateContext(databaseName, tenantContext);
        var reader = new CustomerReadService(db, new NoopProtector());

        var detail = await reader.GetDetailByIdAsync(tenantId, customerId);

        Assert.NotNull(detail);
        Assert.Equal(customerId, detail!.Id);

        var address = Assert.Single(detail.Addresses);
        Assert.Equal("123 Main St", address.Line1);
        Assert.Equal("FL", address.Region);
        Assert.True(address.IsPrimary);

        var contact = Assert.Single(detail.ContactPoints);
        Assert.Equal("+13055551234", contact.Value);
        Assert.Equal("cell", contact.Label);

        var relation = Assert.Single(detail.Relations);
        Assert.Equal("Ada Byron Lovelace", relation.DisplayName); // compuesto en memoria (propiedad computada)
        Assert.Equal(RelationshipKind.Spouse, relation.RelationshipKind);
        Assert.True(relation.IsActive);

        Assert.NotNull(detail.FiscalProfile);
        Assert.Equal("6789", detail.FiscalProfile!.TaxIdentifierLast4); // enmascarado
        Assert.True(detail.FiscalProfile.HasRefundBankInfo);
        Assert.Equal(FilingStatus.Single, detail.FiscalProfile.FilingStatus);
        Assert.Equal(FiscalSubjectKind.Individual, detail.FiscalProfile.SubjectKind);
    }

    [Fact]
    public async Task GetDetailByIdAsync_returns_empty_collections_and_null_fiscal_when_none()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var byUser = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        Guid customerId;
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var customer = NewCustomer(tenantId, byUser);
            await seedDb.Customers.AddAsync(customer);
            await seedDb.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using var db = CreateContext(databaseName, tenantContext);
        var reader = new CustomerReadService(db, new NoopProtector());

        var detail = await reader.GetDetailByIdAsync(tenantId, customerId);

        Assert.NotNull(detail);
        Assert.Empty(detail!.Addresses);
        Assert.Empty(detail.ContactPoints);
        Assert.Empty(detail.Relations);
        Assert.Null(detail.FiscalProfile);
    }

    [Fact]
    public async Task GetDetailByIdAsync_returns_null_for_a_customer_of_another_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var byUser = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        Guid customerId;
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var customer = NewCustomer(tenantId, byUser);
            await seedDb.Customers.AddAsync(customer);
            await seedDb.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using var db = CreateContext(databaseName, tenantContext);
        var reader = new CustomerReadService(db, new NoopProtector());

        // Pide el mismo cliente pero con el tenant de otra oficina: no debe verlo.
        var detail = await reader.GetDetailByIdAsync(otherTenant, customerId);

        Assert.Null(detail);
    }

    [Fact]
    public async Task GetDetailByIdAsync_surfaces_the_date_of_birth()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var byUser = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);
        var dob = new DateOnly(1985, 3, 15);

        Guid customerId;
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var customer = NewCustomer(tenantId, byUser);
            customer.ChangeDateOfBirth(dob, byUser);
            await seedDb.Customers.AddAsync(customer);
            await seedDb.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using var db = CreateContext(databaseName, tenantContext);
        var reader = new CustomerReadService(db, new NoopProtector());

        var detail = await reader.GetDetailByIdAsync(tenantId, customerId);

        Assert.NotNull(detail);
        Assert.Equal(dob, detail!.DateOfBirth);
        // Partes del nombre (para que el form de edición prefille sin partir el DisplayName).
        Assert.Equal("Grace", detail.FirstName);
        Assert.Equal("Hopper", detail.LastName);
    }

    [Fact]
    public async Task ListOccupationsAsync_returns_active_ordered_and_optionally_filtered()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(Guid.NewGuid());

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var mechanic = Occupation.Create(Guid.NewGuid(), "Mechanic", displayOrder: 2).Value;
            var accountant = Occupation.Create(Guid.NewGuid(), "Accountant", displayOrder: 1).Value;
            var retired = Occupation.Create(Guid.NewGuid(), "Retired teacher", displayOrder: 3).Value;
            retired.Deactivate(); // no debe listarse
            await seedDb.Occupations.AddRangeAsync(mechanic, accountant, retired);
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName, tenantContext);
        var reader = new CustomerReadService(db, new NoopProtector());

        var all = await reader.ListOccupationsAsync(null);
        Assert.Equal(2, all.Count); // el desactivado queda fuera
        Assert.Equal("Accountant", all[0].Name); // DisplayOrder 1 primero
        Assert.Equal("Mechanic", all[1].Name);

        // InMemory usa Contains ordinal (case-sensitive); en SQL Server la collation lo hace
        // insensible a mayúsculas. Se usa el caso correcto para probar el filtro de forma portable.
        var filtered = await reader.ListOccupationsAsync("Mech");
        Assert.Equal("Mechanic", Assert.Single(filtered).Name);
    }

    [Fact]
    public async Task ListBusinessActivitiesAsync_filters_by_code_or_description()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(Guid.NewGuid());

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var landscaping = PrincipalBusinessActivity
                .Create(Guid.NewGuid(), "561730", "Landscaping Services", "Admin")
                .Value;
            var software = PrincipalBusinessActivity
                .Create(Guid.NewGuid(), "541511", "Custom Computer Programming", "Info")
                .Value;
            await seedDb.PrincipalBusinessActivities.AddRangeAsync(landscaping, software);
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName, tenantContext);
        var reader = new CustomerReadService(db, new NoopProtector());

        Assert.Equal(2, (await reader.ListBusinessActivitiesAsync(null)).Count);
        Assert.Equal("561730", Assert.Single(await reader.ListBusinessActivitiesAsync("5617")).NaicsCode); // por código
        Assert.Equal("541511", Assert.Single(await reader.ListBusinessActivitiesAsync("Programming")).NaicsCode); // por descripción
    }
}
