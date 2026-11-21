using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Queries;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// Solicitudes dueñas de archivos por id, contra el SQL Server real: original, sellado, certificado o copia del
/// tenant, con sus colecciones cargadas; nunca las de otro tenant. La base es temporal por corrida.
/// </summary>
public sealed class SignatureOriginalFileQueryIntegrationTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Luis = Guid.NewGuid();

    [Fact]
    public async Task Finds_the_tenant_requests_that_own_any_of_the_files()
    {
        var request = Sealed();
        var copy = Guid.NewGuid();
        request.RecordCustomerCopy(request.Documents[0].SealedFileId!.Value, Ana, copy, DateTime.UtcNow);
        var foreign = Sealed();
        await SeedAsync(request);
        await SeedAsync(foreign);
        await using var db = NewContext(request.TenantId);
        var query = new SignatureOriginalFileQuery(db);

        foreach (
            var fileId in new[]
            {
                request.Documents[1].OriginalFileId,
                request.Documents[0].SealedFileId!.Value,
                request.CertificateFileId!.Value,
                copy,
            }
        )
        {
            var found = Assert.Single(
                await query.ListOwningFilesAsync(request.TenantId, [fileId, foreign.Documents[0].OriginalFileId])
            );
            Assert.Equal(request.Id, found.Id);
            Assert.Equal(2, found.Documents.Count);
            Assert.Equal(2, found.Signers.Count);
            Assert.Single(found.CustomerCopies);
        }

        Assert.Empty(
            await query.ListOwningFilesAsync(
                request.TenantId,
                [foreign.Documents[0].SealedFileId!.Value, Guid.NewGuid()]
            )
        );
    }

    private static SignatureRequest Sealed() =>
        new DeliveryTestKit(
            sendCertificate: true,
            certificateMode: CertificateGenerationMode.SingleForRequest,
            firstSignerCustomerId: Ana,
            secondSignerCustomerId: Luis
        )
            .Seal()
            .Request;

    private static async Task SeedAsync(SignatureRequest request)
    {
        await using var db = NewContext(request.TenantId);
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
    }

    private static SignatureDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<SignatureDbContext>()
                .UseSqlServer(SignatureTestDatabase.ConnectionString)
                .Options,
            new FixedTenant(tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
