using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Relations;
using TaxVision.Customer.Infrastructure.Imports;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using TaxVision.Customer.Infrastructure.Security;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Persistence;

/// <summary>
/// B-9/X-9 — lookup dual de blind index durante la ventana de rotación, contra SQL real. Una fila guardada con
/// la clave vieja se encuentra por el candidato de la clave anterior cuando ésta está configurada; sin clave
/// anterior el comportamiento es el de hoy (la búsqueda por la clave actual casa la fila escrita con esa clave).
/// Caracteriza también el detector de duplicados sin rotación.
/// </summary>
[Collection(nameof(CustomerFiscalSqlCollection))]
public sealed class CustomerFiscalDualLookupTests
{
    private const string Ssn = "123456789";
    private const string RelationSsn = "223456789";

    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=TaxVision_Customer;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly byte[] OldKey = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] NewKey = RandomNumberGenerator.GetBytes(32);
    private const short OldVersion = 2;
    private const short NewVersion = 3;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _byUser = Guid.NewGuid();

    // Rotación en curso: clave nueva actual, vieja como anterior.
    private AesGcmSensitiveDataProtector Rotating => new(OptionsFor(NewKey, NewVersion, OldKey, OldVersion));

    // Sin rotación: la clave vieja es la única (= el estado de hoy, antes de rotar).
    private AesGcmSensitiveDataProtector NoRotation => new(OptionsFor(OldKey, OldVersion, null, null));

    [Fact]
    public async Task Customer_lookup_finds_a_row_stored_under_the_previous_key_during_rotation()
    {
        try
        {
            var customerId = await SeedCustomerAsync();
            var repo = new CustomerRepository(CreateContext());

            // Candidatos de la clave actual ∪ anterior: la fila (BI con clave vieja) casa por el segundo.
            var hit = await repo.FindCustomerIdByFiscalBlindIndexAsync(
                _tenantId,
                Rotating.ComputeBlindIndexCandidates(Ssn, _tenantId),
                excludeCustomerId: null,
                CancellationToken.None
            );

            Assert.Equal(customerId, hit);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Customer_lookup_without_a_previous_key_behaves_like_today()
    {
        try
        {
            var customerId = await SeedCustomerAsync();
            var repo = new CustomerRepository(CreateContext());

            // Sin clave anterior, un solo candidato (la clave con la que se escribió la fila) la encuentra.
            var found = await repo.FindCustomerIdByFiscalBlindIndexAsync(
                _tenantId,
                NoRotation.ComputeBlindIndexCandidates(Ssn, _tenantId),
                excludeCustomerId: null,
                CancellationToken.None
            );
            Assert.Equal(customerId, found);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Relation_lookup_finds_a_row_stored_under_the_previous_key_during_rotation()
    {
        try
        {
            var relationId = await SeedCustomerWithRelationAsync();
            var repo = new CustomerRepository(CreateContext());

            var hit = await repo.FindRelationIdByFiscalBlindIndexAsync(
                _tenantId,
                Rotating.ComputeBlindIndexCandidates(RelationSsn, _tenantId),
                excludeRelationId: null,
                CancellationToken.None
            );

            Assert.Equal(relationId, hit);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Duplicate_detector_matches_through_the_previous_key_during_rotation()
    {
        try
        {
            var customerId = await SeedCustomerAsync();
            var detector = new SqlServerCustomerDuplicateDetector(CreateContext(), Rotating);

            var matches = await detector.FindDuplicatesAsync(_tenantId, [Row(Ssn)], CancellationToken.None);

            var match = Assert.Single(matches);
            Assert.Equal(customerId, match.ExistingCustomerId);
            Assert.Equal("TaxIdentifier", match.MatchedBy);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Duplicate_detector_without_rotation_still_matches_on_the_current_key()
    {
        try
        {
            var customerId = await SeedCustomerAsync();
            var detector = new SqlServerCustomerDuplicateDetector(CreateContext(), NoRotation);

            var matches = await detector.FindDuplicatesAsync(_tenantId, [Row(Ssn)], CancellationToken.None);

            var match = Assert.Single(matches);
            Assert.Equal(customerId, match.ExistingCustomerId);
            Assert.Equal("TaxIdentifier", match.MatchedBy);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Check_exists_finds_the_tax_identifier_through_the_previous_key_during_rotation()
    {
        try
        {
            var customerId = await SeedCustomerAsync();
            var readService = new CustomerReadService(CreateContext(), Rotating);

            var result = await readService.CheckExistsAsync(_tenantId, email: null, taxIdentifier: Ssn);

            Assert.True(result.TaxIdentifierExists);
            Assert.Equal(customerId, result.ExistingCustomerId);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private static ImportCustomerRow Row(string ssn) => new() { RowNumber = 1, TaxIdentifier = ssn };

    private async Task<Guid> SeedCustomerAsync()
    {
        var old = NoRotation;
        await using var db = CreateContext();
        var customer = NewCustomer();
        customer.SetFiscalProfile(
            subjectKind: FiscalSubjectKind.Individual,
            taxIdentifierCipher: old.Protect(Ssn),
            taxIdentifierBlindIndex: old.ComputeBlindIndex(Ssn, _tenantId),
            taxIdentifierLast4: Ssn[^4..],
            filingStatus: null,
            priorYearAgi: null,
            isReturningCustomer: false,
            refundBankAccountCipher: null,
            refundBankRoutingCipher: null,
            byUserId: _byUser,
            taxIdentifierKeyVersion: OldVersion
        );
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<Guid> SeedCustomerWithRelationAsync()
    {
        var old = NoRotation;
        await using var db = CreateContext();
        var customer = NewCustomer();
        var rel = customer
            .AddRelation(
                RelationshipKind.Spouse,
                RelationPurpose.TaxHouseholdMember,
                PersonalName.Create("Spouse", "Rel").Value,
                null,
                null,
                null,
                null,
                _byUser
            )
            .Value;
        db.Customers.Add(customer);
        // La relación obtiene su Id generado aquí; recién entonces su perfil fiscal puede apuntarla.
        await db.SaveChangesAsync();

        rel.SetFiscalProfile(
            role: TaxRelationshipRole.Spouse,
            taxIdentifierCipher: old.Protect(RelationSsn),
            taxIdentifierBlindIndex: old.ComputeBlindIndex(RelationSsn, _tenantId),
            taxIdentifierLast4: RelationSsn[^4..],
            taxYear: 2025,
            qualifiesAsDependent: false,
            livedWithTaxpayer: true,
            byUserId: _byUser,
            taxIdentifierKeyVersion: OldVersion
        );
        await db.SaveChangesAsync();
        return rel.Id;
    }

    private async Task CleanupAsync()
    {
        await using var db = CreateContext();
        await db
            .CustomerRelationFiscalProfiles.IgnoreQueryFilters()
            .Where(fp => fp.TenantId == _tenantId)
            .ExecuteDeleteAsync();
        await db.CustomerRelations.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.CustomerFiscalProfiles.IgnoreQueryFilters().Where(fp => fp.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.Customers.IgnoreQueryFilters().Where(c => c.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    private DomainCustomer NewCustomer()
    {
        var name = PersonalName.Create("Dual", "Lookup").Value;
        var email = EmailAddress.Create($"dual-{Guid.NewGuid():N}@example.test").Value;
        return DomainCustomer
            .Register(_tenantId, CustomerKind.Individual, name, null, email, null, Language.En, _byUser)
            .Value;
    }

    private static IOptions<RotatingSecretProtectionOptions> OptionsFor(
        byte[] currentKey,
        short currentVersion,
        byte[]? previousKey,
        short? previousVersion
    ) =>
        Options.Create(
            new RotatingSecretProtectionOptions
            {
                MasterKey = Convert.ToBase64String(currentKey),
                MasterKeyVersion = currentVersion,
                PreviousMasterKey = previousKey is null ? null : Convert.ToBase64String(previousKey),
                PreviousMasterKeyVersion = previousVersion,
            }
        );

    private CustomerDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<CustomerDbContext>().UseSqlServer(ConnectionString).Options,
            new FixedTenantContext(_tenantId)
        );

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => tenantId != Guid.Empty;

        public void SetTenant(Guid value) { }
    }
}
