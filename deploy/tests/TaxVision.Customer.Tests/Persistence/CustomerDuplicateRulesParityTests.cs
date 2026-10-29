using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Imports;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Security;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Persistence;

/// <summary>
/// B-17/B-29, contra SQL real: el import (por lotes) y el alta (uno a uno) deciden lo mismo con los mismos datos.
///
/// <para>
/// Caracterización previa al cambio (corrida en verde contra el código anterior): el import marcaba duplicado
/// por teléfono solo (la API no), ignoraba el segundo nombre y la razón social en Nombre + DOB (la API no), y
/// sin tenant ambiente ninguno de los dos caminos veía a los clientes existentes.
/// </para>
/// </summary>
[Collection(nameof(CustomerFiscalSqlCollection))]
public sealed class CustomerDuplicateRulesParityTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=TaxVision_Customer;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly DateOnly Dob = new(1990, 12, 10);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _byUser = Guid.NewGuid();

    [Fact]
    public async Task Phone_alone_is_not_a_duplicate_on_either_path()
    {
        try
        {
            await SeedPersonAsync("ada@example.test", "+13055550123");

            var (import, api) = await BothAsync(
                Person("Grace", null, "Hopper", "grace@example.test", phone: "(305) 555-0123"),
                new CustomerDuplicateCandidate("grace@example.test", "+13055550123", "Grace Hopper", null)
            );

            Assert.Null(import);
            Assert.Null(api);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Phone_plus_name_is_a_duplicate_on_both_paths()
    {
        try
        {
            var existingId = await SeedPersonAsync("ada@example.test", "+13055550123");

            var (import, api) = await BothAsync(
                Person("ADA", null, "lovelace", "other@example.test", phone: "305-555-0123"),
                new CustomerDuplicateCandidate("other@example.test", "+13055550123", "ADA lovelace", null)
            );

            AssertSameMatch(existingId, "Phone+Name", import, api);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Name_plus_dob_uses_the_full_display_name_on_both_paths()
    {
        try
        {
            var existingId = await SeedPersonAsync("ada@example.test", phone: null, middleName: "Maria", dob: Dob);

            var (import, api) = await BothAsync(
                Person("Ada", "Maria", "Lovelace", "other@example.test", dob: "1990-12-10"),
                new CustomerDuplicateCandidate("other@example.test", null, "Ada Maria Lovelace", Dob)
            );

            AssertSameMatch(existingId, "Name+DOB", import, api);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Name_without_the_middle_name_is_not_a_name_plus_dob_duplicate_on_either_path()
    {
        try
        {
            await SeedPersonAsync("ada@example.test", phone: null, middleName: "Maria", dob: Dob);

            var (import, api) = await BothAsync(
                Person("Ada", null, "Lovelace", "other@example.test", dob: "1990-12-10"),
                new CustomerDuplicateCandidate("other@example.test", null, "Ada Lovelace", Dob)
            );

            Assert.Null(import);
            Assert.Null(api);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task A_business_matches_by_legal_name_plus_dob_on_both_paths()
    {
        try
        {
            var existingId = await SeedBusinessAsync("acme@example.test", "Acme Holdings LLC", Dob);

            var (import, api) = await BothAsync(
                new ImportCustomerRow
                {
                    RowNumber = 1,
                    Kind = "Business",
                    LegalName = " acme holdings llc ",
                    BusinessStructure = "Llc",
                    Email = "billing@example.test",
                    DateOfBirth = "1990-12-10",
                },
                new CustomerDuplicateCandidate("billing@example.test", null, "Acme Holdings LLC", Dob)
            );

            AssertSameMatch(existingId, "Name+DOB", import, api);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Email_wins_and_ignores_case_on_both_paths()
    {
        try
        {
            var existingId = await SeedPersonAsync("ada@example.test", "+13055550123");

            var (import, api) = await BothAsync(
                Person("Someone", null, "Else", "  ADA@Example.TEST ", phone: "(305) 555-0123"),
                new CustomerDuplicateCandidate("ADA@Example.TEST", "+13055550123", "Someone Else", null)
            );

            AssertSameMatch(existingId, "Email", import, api);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task An_archived_customer_is_not_a_duplicate_on_either_path()
    {
        try
        {
            await SeedPersonAsync("ada@example.test", phone: null, archived: true);

            var (import, api) = await BothAsync(
                Person("Ada", null, "Lovelace", "ada@example.test"),
                new CustomerDuplicateCandidate("ada@example.test", null, "Ada Lovelace", null)
            );

            Assert.Null(import);
            Assert.Null(api);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    /// <summary>B-29: el import corre en un scope sin tenant ambiente; el filtro fail-closed no debe esconder nada.</summary>
    [Fact]
    public async Task Without_an_ambient_tenant_both_paths_still_see_existing_customers()
    {
        try
        {
            var existingId = await SeedPersonAsync("ada@example.test", phone: null);

            var (import, api) = await BothAsync(
                Person("Ada", null, "Lovelace", "ada@example.test"),
                new CustomerDuplicateCandidate("ada@example.test", null, "Ada Lovelace", null),
                ambientTenant: Guid.Empty
            );

            AssertSameMatch(existingId, "Email", import, api);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Customers_of_another_tenant_are_never_duplicates()
    {
        var otherTenant = Guid.NewGuid();
        try
        {
            await SeedPersonAsync("ada@example.test", "+13055550123", tenantId: otherTenant);

            var (import, api) = await BothAsync(
                Person("Ada", null, "Lovelace", "ada@example.test", phone: "(305) 555-0123"),
                new CustomerDuplicateCandidate("ada@example.test", "+13055550123", "Ada Lovelace", null),
                ambientTenant: Guid.Empty
            );

            Assert.Null(import);
            Assert.Null(api);
        }
        finally
        {
            await CleanupAsync(otherTenant);
        }
    }

    private static void AssertSameMatch(Guid existingId, string matchedBy, DuplicateMatch? import, DuplicateMatch? api)
    {
        Assert.NotNull(import);
        Assert.NotNull(api);
        Assert.Equal(existingId, import.ExistingCustomerId);
        Assert.Equal(existingId, api.ExistingCustomerId);
        Assert.Equal(matchedBy, import.MatchedBy);
        Assert.Equal(matchedBy, api.MatchedBy);
    }

    private static ImportCustomerRow Person(
        string first,
        string? middle,
        string last,
        string email,
        string? phone = null,
        string? dob = null
    ) =>
        new()
        {
            RowNumber = 1,
            Kind = "Individual",
            FirstName = first,
            MiddleName = middle,
            LastName = last,
            Email = email,
            Phone = phone,
            DateOfBirth = dob,
        };

    private async Task<(DuplicateMatch? Import, DuplicateMatch? Api)> BothAsync(
        ImportCustomerRow row,
        CustomerDuplicateCandidate candidate,
        Guid? ambientTenant = null
    )
    {
        var detector = new SqlServerCustomerDuplicateDetector(CreateContext(ambientTenant ?? _tenantId), Protector());
        var import = (await detector.FindDuplicatesAsync(_tenantId, [row], CancellationToken.None)).SingleOrDefault();
        var api = await detector.FindDuplicateAsync(
            _tenantId,
            candidate,
            excludeCustomerId: null,
            CancellationToken.None
        );
        return (import, api);
    }

    private async Task<Guid> SeedPersonAsync(
        string email,
        string? phone,
        string? middleName = null,
        DateOnly? dob = null,
        bool archived = false,
        Guid? tenantId = null
    )
    {
        var tenant = tenantId ?? _tenantId;
        await using var db = CreateContext(tenant);
        var customer = DomainCustomer
            .Register(
                tenant,
                CustomerKind.Individual,
                PersonalName.Create("Ada", "Lovelace", middleName).Value,
                null,
                EmailAddress.Create(email).Value,
                phone is null ? null : PhoneNumber.Create(phone).Value,
                Language.En,
                _byUser,
                dateOfBirth: dob
            )
            .Value;
        if (archived)
            Assert.True(customer.Archive(_byUser).IsSuccess);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<Guid> SeedBusinessAsync(string email, string legalName, DateOnly dob)
    {
        await using var db = CreateContext(_tenantId);
        var customer = DomainCustomer
            .Register(
                _tenantId,
                CustomerKind.Business,
                null,
                BusinessIdentity.Create(legalName, BusinessStructure.Llc).Value,
                EmailAddress.Create(email).Value,
                null,
                Language.En,
                _byUser,
                dateOfBirth: dob
            )
            .Value;
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task CleanupAsync(Guid? tenantId = null)
    {
        var tenant = tenantId ?? _tenantId;
        await using var db = CreateContext(tenant);
        await db.CustomerFiscalProfiles.IgnoreQueryFilters().Where(fp => fp.TenantId == tenant).ExecuteDeleteAsync();
        await db.Customers.IgnoreQueryFilters().Where(c => c.TenantId == tenant).ExecuteDeleteAsync();
    }

    private static AesGcmSensitiveDataProtector Protector() =>
        new(
            Options.Create(
                new RotatingSecretProtectionOptions
                {
                    MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    MasterKeyVersion = 1,
                }
            )
        );

    private static CustomerDbContext CreateContext(Guid ambientTenant) =>
        new(
            new DbContextOptionsBuilder<CustomerDbContext>().UseSqlServer(ConnectionString).Options,
            new FixedTenantContext(ambientTenant)
        );

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => tenantId != Guid.Empty;

        public void SetTenant(Guid value) { }
    }
}
