using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Imports;
using TaxVision.Customer.Domain.Relations;
using TaxVision.Customer.Infrastructure.Imports;
using TaxVision.Customer.Infrastructure.Persistence;
using Xunit;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// Contra SQL Server (base temporal): los únicos ya tomados que el import revisa antes de guardar, y la búsqueda de
/// imports atascados que alimenta el barrido.
/// </summary>
public sealed class CustomerImportResilienceIntegrationTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task Taken_identifiers_include_archived_tax_ids_and_old_key_indexes_but_not_archived_emails()
    {
        var archived = NewCustomer("archived@example.com");
        archived.SetFiscalProfile(
            FiscalSubjectKind.Individual,
            [1],
            "old-123456789",
            "6789",
            null,
            null,
            false,
            null,
            null,
            UserId,
            1
        );
        archived.Archive(UserId);
        var active = NewCustomer("active@example.com");
        active.AddRelation(
            RelationshipKind.Spouse,
            RelationPurpose.TaxHouseholdMember,
            PersonalName.Create("Sam", "Active").Value,
            null,
            null,
            null,
            null,
            UserId
        );
        await SaveAsync(archived, active);
        // Como la API: el perfil fiscal del cónyuge se pone cuando la relación ya tiene Id.
        await using (var seed = NewContext())
        {
            var relation = await seed
                .CustomerRelations.IgnoreQueryFilters()
                .SingleAsync(r => r.CustomerId == active.Id);
            Assert.True(
                relation
                    .SetFiscalProfile(
                        TaxRelationshipRole.Spouse,
                        [1],
                        "bi-987654321",
                        "4321",
                        2026,
                        false,
                        true,
                        UserId
                    )
                    .IsSuccess
            );
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var registry = new SqlServerCustomerImportIdentifierRegistry(db, new PrefixProtector());
        var taken = await registry.FindTakenAsync(
            _tenantId,
            [
                Row(2, "archived@example.com", "123-45-6789", spouseTaxId: "987-65-4321"),
                Row(3, "active@example.com", null),
            ],
            default
        );

        Assert.Equal(archived.Id, taken.TaxIdOwners["bi-123456789"]);
        Assert.True(taken.SpouseTaxIdRelations.ContainsKey("bi-987654321"));
        Assert.Equal(active.Id, taken.EmailOwners["active@example.com"]);
        Assert.False(taken.EmailOwners.ContainsKey("archived@example.com"));
    }

    [Fact]
    public async Task Only_imports_in_progress_without_recent_progress_are_listed_as_stuck()
    {
        var now = DateTime.UtcNow;
        var stuck = Attempt(Guid.NewGuid(), lastProgress: now.AddMinutes(-40));
        var moving = Attempt(Guid.NewGuid(), lastProgress: now.AddMinutes(-2));
        var finished = Attempt(Guid.NewGuid(), lastProgress: now.AddMinutes(-40));
        finished.Complete();
        await using (var db = NewContext())
        {
            db.CustomerImportAttempts.AddRange(stuck, moving, finished);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var ids = await new CustomerImportRecoveryStore(read).ListStuckIdsAsync(now.AddMinutes(-15), 50, default);

        Assert.Contains(stuck.Id, ids);
        Assert.DoesNotContain(moving.Id, ids);
        Assert.DoesNotContain(finished.Id, ids);
    }

    private DomainCustomer NewCustomer(string email)
    {
        var customer = DomainCustomer
            .Register(
                _tenantId,
                CustomerKind.Individual,
                PersonalName.Create("Import", email.Split('@')[0]).Value,
                null,
                EmailAddress.Create(email).Value,
                null,
                Language.En,
                UserId
            )
            .Value;
        customer.ClearDomainEvents();
        return customer;
    }

    private static CustomerImportAttempt Attempt(Guid tenantId, DateTime lastProgress)
    {
        var attempt = CustomerImportAttempt
            .Create(tenantId, UserId, $"key-{Guid.NewGuid():N}", DuplicateStrategy.Skip, ImportSourceKind.Csv, "a.csv")
            .Value;
        attempt.Start();
        attempt.MoveToApplying();
        attempt.Resume(lastProgress);
        return attempt;
    }

    private static ImportCustomerRow Row(int number, string email, string? taxId, string? spouseTaxId = null) =>
        new()
        {
            RowNumber = number,
            Kind = "Individual",
            FirstName = "Row",
            LastName = number.ToString(),
            Email = email,
            TaxIdentifier = taxId,
            SpouseTaxIdentifier = spouseTaxId,
        };

    private async Task SaveAsync(params DomainCustomer[] customers)
    {
        await using var db = NewContext();
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();
    }

    private CustomerDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CustomerDbContext>()
                .UseSqlServer(CustomerIntegrationDatabase.ConnectionString)
                .Options,
            new FixedTenant(_tenantId)
        );

    // Llave actual "bi-", anterior "old-": prueba el lookup dual durante una rotación.
    private sealed class PrefixProtector : ISensitiveDataProtector
    {
        public short CurrentKeyVersion => 1;

        public byte[] Protect(string plainText) => [1];

        public string Unprotect(byte[] cipher) => string.Empty;

        public string ComputeBlindIndex(string plainText, Guid tenantId) => $"bi-{plainText}";

        public IReadOnlyCollection<string> ComputeBlindIndexCandidates(string plainText, Guid tenantId) =>
            [$"bi-{plainText}", $"old-{plainText}"];
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
