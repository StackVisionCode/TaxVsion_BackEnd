using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Queries.List;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Queries;
using Xunit;

namespace TaxVision.Signature.Tests.Persistence;

/// <summary>
/// F2.5: el autor SIEMPRE ve sus propios Draft, aunque los firmantes aún no estén mapeados a
/// un cliente asignado al actor. Sin esta rama, un borrador autoguardado sin signers desaparecería
/// de la lista de su propio creador.
/// </summary>
public sealed class SignatureRequestReadServiceAuthorVisibilityTests
{
    private static readonly Guid Tenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Author_sees_their_own_draft_even_without_signers()
    {
        var author = Guid.NewGuid();
        var db = NewContext();
        var draft = Draft(author);
        await Seed(db, draft);

        var result = await NewService(db, enabled: true).ListAsync(Query(author, canViewAll: false));

        Assert.Single(result.Items);
        Assert.True(result.Items[0].IsOwnedByActor);
    }

    [Fact]
    public async Task Peer_without_view_all_does_not_see_someone_elses_draft()
    {
        var author = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var db = NewContext();
        await Seed(db, Draft(author));

        var result = await NewService(db, enabled: true).ListAsync(Query(peer, canViewAll: false));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task CanViewAll_sees_the_draft_without_being_the_author()
    {
        var author = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var db = NewContext();
        await Seed(db, Draft(author));

        var result = await NewService(db, enabled: true).ListAsync(Query(peer, canViewAll: true));

        Assert.Single(result.Items);
        Assert.False(result.Items[0].IsOwnedByActor);
    }

    // ----- helpers -----

    private static SignatureRequest Draft(Guid createdByUserId) =>
        SignatureRequest
            .CreateDraft(
                Tenant,
                createdByUserId,
                "Consent 2026",
                null,
                "Fiscal",
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;

    private static ListSignatureRequestsQuery Query(Guid actorUserId, bool canViewAll) =>
        new(
            TenantId: Tenant,
            Status: null,
            Category: null,
            Page: 1,
            PageSize: 20,
            ActorUserId: actorUserId,
            CanViewAll: canViewAll
        );

    private static SignatureRequestReadService NewService(SignatureDbContext db, bool enabled) =>
        new(db, Options.Create(new SignatureVisibilityOptions { Enabled = enabled }));

    private static SignatureDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SignatureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SignatureDbContext(options, new StubTenantContext(Tenant));
    }

    private static async Task Seed(SignatureDbContext db, params SignatureRequest[] requests)
    {
        db.SignatureRequests.AddRange(requests);
        await db.SaveChangesAsync();
    }

    private sealed class StubTenantContext(Guid? tenantId = null) : ITenantContext
    {
        public Guid TenantId => tenantId ?? throw new InvalidOperationException("No tenant set.");
        public bool HasTenant => tenantId.HasValue;

        public void SetTenant(Guid value) => throw new NotSupportedException();
    }
}
