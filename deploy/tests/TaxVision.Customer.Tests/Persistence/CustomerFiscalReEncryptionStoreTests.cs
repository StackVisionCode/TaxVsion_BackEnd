using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Customers.FiscalProfiles;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Relations;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Security;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Persistence;

/// <summary>
/// B-9/X-9 — el sweep re-cifra los perfiles fiscales de cliente y de relación contra SQL real, con interrupción
/// y reanudación (idempotente y reanudable sin cursor). Las filas se siembran en una versión de clave (2) con
/// clave de prueba y se re-cifran a otra (1), ambas de prueba: el barrido global no toca los datos del app, que
/// quedan fuera del filtro (su versión ya es la actual del barrido). Tras el sweep el BI almacenado casa con el
/// que produce la clave actual, y las cifras (tax identifier + banco) descifran con la clave actual a secas.
/// </summary>
[Collection(nameof(CustomerFiscalSqlCollection))]
public sealed class CustomerFiscalReEncryptionStoreTests
{
    private const int CustomerCount = 300;
    private const int RelationCount = 40;
    private const short SeedVersion = 2;
    private const short TargetVersion = 1;

    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=TaxVision_Customer;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly byte[] TargetKey = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] SeedKey = RandomNumberGenerator.GetBytes(32);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _byUser = Guid.NewGuid();

    [Fact]
    public async Task The_sweep_reencrypts_fiscal_rows_resuming_after_an_interruption()
    {
        try
        {
            await SeedAsync();

            Assert.Equal(CustomerCount, await CountCustomersAtAsync(SeedVersion));
            Assert.Equal(RelationCount, await CountRelationsAtAsync(SeedVersion));

            // El censo global ve mis filas en la versión de siembra.
            var census = await CreateStore().CountByKeyVersionAsync();
            Assert.True(census.GetValueOrDefault(SeedVersion) >= CustomerCount + RelationCount);

            // Primera pasada: se cancela apenas se ve avance, dejando filas sin re-cifrar.
            using (var cts = new CancellationTokenSource())
            {
                var run = Task.Run(() => CreateStore().ReEncryptToCurrentAsync(50, cts.Token));
                await WaitUntilProgressAsync(cts);
                try
                {
                    await run;
                }
                catch (OperationCanceledException) { }
            }

            Assert.True(
                await CountCustomersAtAsync(SeedVersion) < CustomerCount,
                "Expected the interrupted run to re-encrypt at least one batch."
            );

            // Reanudación: termina lo que faltaba. Idempotente: las ya hechas salen del filtro.
            await CreateStore().ReEncryptToCurrentAsync(100, CancellationToken.None);

            Assert.Equal(0, await CountCustomersAtAsync(SeedVersion));
            Assert.Equal(0, await CountRelationsAtAsync(SeedVersion));
            Assert.Equal(CustomerCount, await CountCustomersAtAsync(TargetVersion));
            Assert.Equal(RelationCount, await CountRelationsAtAsync(TargetVersion));
            await AssertSamplesDecryptWithCurrentKeyAsync();

            // Correr de nuevo no reescribe las filas ya en la versión actual.
            var report = await CreateStore().ReEncryptToCurrentAsync(100, CancellationToken.None);
            Assert.Equal(0, report.Total);
            Assert.Equal(CustomerCount, await CountCustomersAtAsync(TargetVersion));
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task SeedAsync()
    {
        var seed = Protector(SeedKey, SeedVersion);
        await using var db = CreateContext();

        // Fase 1: customers (con su perfil fiscal, Id de customer client-set) y relaciones "desnudas". La relación
        // obtiene su Id generado en este save; recién entonces su perfil fiscal puede apuntarlo (fase 2).
        var relations = new List<CustomerRelation>(RelationCount);
        for (var index = 0; index < CustomerCount; index++)
        {
            var ssn = Ssn(index);
            var customer = NewCustomer(index);
            customer.SetFiscalProfile(
                subjectKind: FiscalSubjectKind.Individual,
                taxIdentifierCipher: seed.Protect(ssn),
                taxIdentifierBlindIndex: seed.ComputeBlindIndex(ssn, _tenantId),
                taxIdentifierLast4: ssn[^4..],
                filingStatus: null,
                priorYearAgi: null,
                isReturningCustomer: false,
                refundBankAccountCipher: index < RelationCount ? seed.Protect($"ACCT{index:D6}") : null,
                refundBankRoutingCipher: null,
                byUserId: _byUser,
                taxIdentifierKeyVersion: SeedVersion
            );

            if (index < RelationCount)
                relations.Add(
                    customer
                        .AddRelation(
                            RelationshipKind.Spouse,
                            RelationPurpose.TaxHouseholdMember,
                            PersonalName.Create("Spouse", $"Rel{index}").Value,
                            null,
                            null,
                            null,
                            null,
                            _byUser
                        )
                        .Value
                );

            db.Customers.Add(customer);
        }
        await db.SaveChangesAsync();

        // Fase 2: perfil fiscal de cada relación, ya con el Id real de la relación (mismo contexto, sigue tracked).
        for (var index = 0; index < relations.Count; index++)
        {
            var relSsn = RelationSsn(index);
            relations[index]
                .SetFiscalProfile(
                    role: TaxRelationshipRole.Spouse,
                    taxIdentifierCipher: seed.Protect(relSsn),
                    taxIdentifierBlindIndex: seed.ComputeBlindIndex(relSsn, _tenantId),
                    taxIdentifierLast4: relSsn[^4..],
                    taxYear: 2025,
                    qualifiesAsDependent: false,
                    livedWithTaxpayer: true,
                    byUserId: _byUser,
                    taxIdentifierKeyVersion: SeedVersion
                );
        }
        await db.SaveChangesAsync();
    }

    private async Task WaitUntilProgressAsync(CancellationTokenSource cts)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (await CountCustomersAtAsync(SeedVersion) < CustomerCount)
            {
                await cts.CancelAsync();
                return;
            }
            await Task.Delay(10);
        }
        await cts.CancelAsync();
    }

    private async Task AssertSamplesDecryptWithCurrentKeyAsync()
    {
        var currentOnly = Protector(TargetKey, TargetVersion); // sin clave anterior: la retirada ya no hace falta
        await using var db = CreateContext();

        var customerLast4 = Ssn(0)[^4..];
        var relationLast4 = RelationSsn(0)[^4..];

        var customer = await db
            .CustomerFiscalProfiles.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(fp => fp.TenantId == _tenantId && fp.TaxIdentifierLast4 == customerLast4);
        Assert.Equal(Ssn(0), currentOnly.Unprotect(customer.TaxIdentifierCipher));
        Assert.Equal(currentOnly.ComputeBlindIndex(Ssn(0), _tenantId), customer.TaxIdentifierBlindIndex);
        Assert.Equal(TargetVersion, customer.TaxIdentifierKeyVersion);
        Assert.NotNull(customer.RefundBankAccountCipher);
        Assert.Equal("ACCT000000", currentOnly.Unprotect(customer.RefundBankAccountCipher!));

        var relation = await db
            .CustomerRelationFiscalProfiles.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(fp => fp.TenantId == _tenantId && fp.TaxIdentifierLast4 == relationLast4);
        Assert.Equal(RelationSsn(0), currentOnly.Unprotect(relation.TaxIdentifierCipher));
        Assert.Equal(currentOnly.ComputeBlindIndex(RelationSsn(0), _tenantId), relation.TaxIdentifierBlindIndex);
        Assert.Equal(TargetVersion, relation.TaxIdentifierKeyVersion);
    }

    private async Task<int> CountCustomersAtAsync(short version)
    {
        await using var db = CreateContext();
        return await db
            .CustomerFiscalProfiles.IgnoreQueryFilters()
            .CountAsync(fp => fp.TenantId == _tenantId && fp.TaxIdentifierKeyVersion == version);
    }

    private async Task<int> CountRelationsAtAsync(short version)
    {
        await using var db = CreateContext();
        return await db
            .CustomerRelationFiscalProfiles.IgnoreQueryFilters()
            .CountAsync(fp => fp.TenantId == _tenantId && fp.TaxIdentifierKeyVersion == version);
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

    private CustomerFiscalReEncryptionStore CreateStore()
    {
        var options = OptionsFor(TargetKey, TargetVersion, SeedKey, SeedVersion);
        var protector = new AesGcmSensitiveDataProtector(options);
        return new CustomerFiscalReEncryptionStore(
            CreateContext(),
            protector,
            new SensitiveDataReEncryptor(protector),
            options
        );
    }

    private static string Ssn(int index) => $"1{index:D8}";

    private static string RelationSsn(int index) => $"2{index:D8}";

    private DomainCustomer NewCustomer(int index)
    {
        var name = PersonalName.Create("Fiscal", $"Cust{index}").Value;
        var email = EmailAddress.Create($"fiscal-{Guid.NewGuid():N}@example.test").Value;
        return DomainCustomer
            .Register(_tenantId, CustomerKind.Individual, name, null, email, null, Language.En, _byUser)
            .Value;
    }

    private static AesGcmSensitiveDataProtector Protector(byte[] key, short version) =>
        new(OptionsFor(key, version, null, null));

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

[CollectionDefinition(nameof(CustomerFiscalSqlCollection), DisableParallelization = true)]
public sealed class CustomerFiscalSqlCollection;
