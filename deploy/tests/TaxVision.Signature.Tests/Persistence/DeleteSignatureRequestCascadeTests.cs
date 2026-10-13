using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Signature.Tests.Persistence;

/// <summary>
/// Borrar un borrador con documento + campos rompía con
/// "The association between entity types 'RequestDocument' and 'SignatureField' has been severed...":
/// SignatureField y PreparerField tienen FK REQUERIDA a RequestDocument con OnDelete(NoAction) (para no
/// crear múltiples caminos de cascada), así que al cascada-borrar los Documents EF severa una relación
/// requerida y lanza antes de que la cascada Signer→Fields borre los fields. El repo ahora los borra
/// primero. El bug sólo aparece con documento + campos reales (los helpers viejos no agregaban documento,
/// por eso nunca lo pegaban).
/// </summary>
public sealed class DeleteSignatureRequestCascadeTests
{
    private static readonly Guid Tenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Remove_borra_un_borrador_con_documento_signer_fields_y_preparer_fields()
    {
        var dbName = Guid.NewGuid().ToString();
        var draft = DraftWithDocumentSignerAndFields();

        await using (var seed = NewContext(dbName))
        {
            seed.SignatureRequests.Add(draft);
            await seed.SaveChangesAsync();
        }

        await using (var db = NewContext(dbName))
        {
            var repo = new SignatureRequestRepository(db);
            var loaded = await repo.GetByIdAsync(Tenant, draft.Id);
            Assert.NotNull(loaded);

            // Antes del fix esto lanzaba InvalidOperationException ("severed required relationship").
            repo.Remove(loaded!);
            await db.SaveChangesAsync();
        }

        await using (var verify = NewContext(dbName))
        {
            Assert.Null(await verify.SignatureRequests.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == draft.Id));
            Assert.Empty(await verify.SignatureFields.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await verify.PreparerFields.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await verify.RequestDocuments.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await verify.Signers.IgnoreQueryFilters().ToListAsync());
        }
    }

    // ---------- helpers ----------

    private static SignatureRequest DraftWithDocumentSignerAndFields()
    {
        var request = SignatureRequest
            .CreateDraft(
                Tenant,
                Guid.NewGuid(),
                "Logos_Certificate",
                null,
                "Fiscal",
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;

        var document = request.AddDocument(Guid.NewGuid(), "Logos_Certificate.pdf", pageCount: 1).Value;
        var signer = request
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Sam Signer").Value, null)
            .Value;

        var position = FieldPosition.Create(1, 0.5, 0.5, 0.1, 0.05).Value;
        // Campo del firmante y campo del preparador: ambos con FK requerida NoAction al documento.
        request.PlaceField(signer.Id, document.Id, SignatureFieldKind.Signature, position, null, false);
        request.PlacePreparerField(document.Id, SignatureFieldKind.Signature, position, null);
        return request;
    }

    private static SignatureDbContext NewContext(string dbName) =>
        new(
            new DbContextOptionsBuilder<SignatureDbContext>().UseInMemoryDatabase(dbName).Options,
            new StubTenantContext(Tenant)
        );

    private sealed class StubTenantContext(Guid? tenantId = null) : ITenantContext
    {
        public Guid TenantId => tenantId ?? throw new InvalidOperationException("No tenant set.");
        public bool HasTenant => tenantId.HasValue;

        public void SetTenant(Guid value) => throw new NotSupportedException();
    }
}
