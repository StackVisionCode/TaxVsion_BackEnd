using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Repositories;
using TaxVision.Signature.Tests.Application.Offboarding;

namespace TaxVision.Signature.Tests.Persistence;

/// <summary>
/// Consultas del trabajo de un empleado en firmas (retiro, pre-flight y candidatos a preparador), contra
/// el modelo EF real: preparador como tipo propio, estados sin enviar y aislamiento por tenant.
/// </summary>
public sealed class SignatureRequestStaffWorkRepositoryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly string _database = Guid.NewGuid().ToString();

    private async Task Seed(params SignatureRequest[] requests)
    {
        await using var db = Context();
        db.SignatureRequests.AddRange(requests);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Counts_pending_preparer_signatures_drafts_as_preparer_and_unsent_authored()
    {
        var signedPreparer = PreparerRequests.Sent(Tenant, Guid.NewGuid(), _user);
        signedPreparer.MarkPreparerSigned(_user, DateTime.UtcNow, null, null);
        await Seed(
            PreparerRequests.Sent(Tenant, Guid.NewGuid(), _user),
            signedPreparer,
            PreparerRequests.DraftWithPreparer(Tenant, Guid.NewGuid(), _user),
            PreparerRequests.Draft(Tenant, _user),
            PreparerRequests.Sent(Tenant, _user, preparerId: null),
            PreparerRequests.Sent(Guid.NewGuid(), Guid.NewGuid(), _user)
        );
        await using var db = Context();

        var counts = await new SignatureRequestStaffWorkRepository(db).CountOffboardingImpactAsync(Tenant, _user);

        Assert.Equal(1, counts.PendingPreparerSignatures);
        Assert.Equal(1, counts.PreparerOnDrafts);
        Assert.Equal(1, counts.DraftRequests);
    }

    [Fact]
    public async Task Lists_unsent_authored_and_open_preparer_work_with_their_graph()
    {
        var draft = PreparerRequests.Draft(Tenant, _user);
        var sent = PreparerRequests.Sent(Tenant, Guid.NewGuid(), _user);
        await Seed(draft, sent, PreparerRequests.Sent(Tenant, _user, preparerId: null));
        await using var db = Context();
        var repository = new SignatureRequestStaffWorkRepository(db);

        var unsent = await repository.ListUnsentAuthoredByAsync(Tenant, _user);
        var preparing = await repository.ListOpenWithPreparerAsync(Tenant, _user);

        Assert.Equal(draft.Id, Assert.Single(unsent).Id);
        var open = Assert.Single(preparing);
        Assert.Equal(sent.Id, open.Id);
        Assert.NotEmpty(open.Signers);
        Assert.NotEmpty(open.Documents);
    }

    [Fact]
    public async Task The_identity_the_user_signed_with_wins_over_a_newer_unsigned_one()
    {
        var signed = PreparerRequests.Sent(Tenant, Guid.NewGuid(), Guid.NewGuid());
        signed.ChangePreparer(PreparerRequests.Identity(_user, "P11112222", "Jane Doe, EA"), null);
        signed.MarkPreparerSigned(_user, DateTime.UtcNow, null, null);
        var newer = PreparerRequests.Draft(Tenant, _user);
        newer.SetPreparer(PreparerRequests.Identity(_user, "P33334444", "Jane D."));
        await Seed(signed, newer);
        await using var db = Context();
        var repository = new SignatureRequestStaffWorkRepository(db);

        var identity = await repository.FindLatestPreparerIdentityAsync(Tenant, _user);
        var users = await repository.ListUsersWithPreparerIdentityAsync(Tenant);

        Assert.Equal("P11112222", identity!.PtinOrEfin);
        Assert.Contains(_user, users);
        Assert.Null(await repository.FindLatestPreparerIdentityAsync(Guid.NewGuid(), _user));
    }

    private SignatureDbContext Context() =>
        new(
            new DbContextOptionsBuilder<SignatureDbContext>().UseInMemoryDatabase(_database).Options,
            new StubTenantContext(Tenant)
        );

    private sealed class StubTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid value) => throw new NotSupportedException();
    }
}
