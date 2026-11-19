using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Queries.Work;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Queries;
using TaxVision.Signature.Tests.Application.Offboarding;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// Work Profile contra el SQL Server real (base temporal BB-10): qué filas lee el proveedor de firmas, con el ABAC
/// del listado para quien mira y nunca de otro tenant. Tenants propios y limpieza por TenantId.
/// </summary>
public sealed class SignatureWorkReadServiceIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _viewer = Guid.NewGuid();
    private readonly DateTime _now = DateTime.UtcNow;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var tenantId in new[] { _tenantId, _otherTenantId })
        {
            await using var db = NewContext(tenantId);
            await db.SignatureRequests.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).ExecuteDeleteAsync();
            await db.Set<CustomerAssignmentProjection>()
                .IgnoreQueryFilters()
                .Where(a => a.TenantId == tenantId)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Reads_open_waiting_and_recently_expired_work_only()
    {
        var asPreparer = Sent(_tenantId, author: Guid.NewGuid(), preparer: _user);
        var asAuthor = Sent(_tenantId, author: _user, preparer: null);
        var recentlyExpired = Sent(_tenantId, author: _user, preparer: null);
        Assert.True(recentlyExpired.MarkExpired(_now.AddDays(-1)).IsSuccess);
        var longExpired = Sent(_tenantId, author: _user, preparer: null);
        Assert.True(longExpired.MarkExpired(_now.AddDays(-(SignatureWorkWindow.RecentlyExpiredDays + 5))).IsSuccess);
        var draft = PreparerRequests.Draft(_tenantId, _user);
        var canceled = Sent(_tenantId, author: _user, preparer: null);
        Assert.True(canceled.Cancel(_now).IsSuccess);
        var preparerSigned = Sent(_tenantId, author: Guid.NewGuid(), preparer: _user);
        Assert.True(preparerSigned.MarkPreparerSigned(_user, _now, null, null).IsSuccess);
        var someoneElse = Sent(_tenantId, author: Guid.NewGuid(), preparer: Guid.NewGuid());
        await SeedAsync(
            _tenantId,
            asPreparer,
            asAuthor,
            recentlyExpired,
            longExpired,
            draft,
            canceled,
            preparerSigned,
            someoneElse
        );

        var rows = await ReadAsync(_tenantId, canViewAll: true);

        Assert.Equal(new[] { asPreparer.Id, asAuthor.Id, recentlyExpired.Id }.Order(), rows.Select(r => r.Id).Order());
        var counts = SignatureWork.Summarize(_user, rows, Window()).Counts.Single();
        Assert.Equal((1, 1, 1), (counts.Open, counts.Waiting, counts.Overdue));
    }

    [Fact]
    public async Task Counts_and_top_follow_the_viewer_assignments_without_view_all()
    {
        var assigned = Guid.NewGuid();
        var notAssigned = Guid.NewGuid();
        var visible = Sent(_tenantId, author: _user, preparer: null, customerId: assigned);
        var hidden = Sent(_tenantId, author: _user, preparer: null, customerId: notAssigned);
        var noCustomer = Sent(_tenantId, author: _user, preparer: null);
        await SeedAsync(_tenantId, visible, hidden, noCustomer);
        await using (var db = NewContext(_tenantId))
        {
            db.Set<CustomerAssignmentProjection>()
                .Add(CustomerAssignmentProjection.Create(_tenantId, assigned, _viewer, _now));
            await db.SaveChangesAsync();
        }

        var restricted = SignatureWork.Summarize(_user, await ReadAsync(_tenantId, canViewAll: false), Window());
        var all = SignatureWork.Summarize(_user, await ReadAsync(_tenantId, canViewAll: true), Window());

        Assert.Equal(1, restricted.Counts.Single().Waiting);
        Assert.Equal(visible.Id, Assert.Single(restricted.Top).Id);
        Assert.Equal(assigned, restricted.Top[0].CustomerId);
        Assert.Equal(3, all.Counts.Single().Waiting);
    }

    [Fact]
    public async Task Another_tenant_is_never_read()
    {
        await SeedAsync(_otherTenantId, Sent(_otherTenantId, author: _user, preparer: _user));

        var rows = await ReadAsync(_tenantId, canViewAll: true);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task Titles_follow_the_list_visibility_of_the_viewer()
    {
        var assigned = Guid.NewGuid();
        var visible = Sent(_tenantId, author: _user, preparer: null, customerId: assigned);
        var hidden = Sent(_tenantId, author: _user, preparer: null, customerId: Guid.NewGuid());
        var ownDraft = PreparerRequests.Draft(_tenantId, _viewer);
        var othersDraft = PreparerRequests.Draft(_tenantId, _user);
        var foreign = Sent(_otherTenantId, author: _viewer, preparer: null);
        await SeedAsync(_tenantId, visible, hidden, ownDraft, othersDraft);
        await SeedAsync(_otherTenantId, foreign);
        await using (var db = NewContext(_tenantId))
        {
            db.Set<CustomerAssignmentProjection>()
                .Add(CustomerAssignmentProjection.Create(_tenantId, assigned, _viewer, _now));
            await db.SaveChangesAsync();
        }
        Guid[] ids = [visible.Id, hidden.Id, ownDraft.Id, othersDraft.Id, foreign.Id, Guid.NewGuid()];

        var restricted = await TitlesAsync(ids, canViewAll: false);
        var all = await TitlesAsync(ids, canViewAll: true);

        Assert.Equal(new[] { visible.Id, ownDraft.Id }.Order(), restricted.Select(r => r.Id).Order());
        Assert.Equal(
            new[] { visible.Id, hidden.Id, ownDraft.Id, othersDraft.Id }.Order(),
            all.Select(r => r.Id).Order()
        );
        Assert.Equal(visible.Title, restricted.Single(r => r.Id == visible.Id).Title);
    }

    // ----- helpers -----

    private async Task<IReadOnlyList<SignatureTitleRow>> TitlesAsync(IReadOnlyList<Guid> ids, bool canViewAll)
    {
        await using var db = NewContext(_tenantId);
        var service = new SignatureWorkReadService(
            db,
            Options.Create(new SignatureVisibilityOptions { Enabled = true })
        );
        return await service.ListTitlesAsync(new SignatureTitleFilter(_tenantId, ids, _viewer, canViewAll));
    }

    private SignatureWorkWindow Window() => SignatureWorkWindow.For(_now, ResolvedTimeZone.Utc);

    private async Task<IReadOnlyList<SignatureWorkRow>> ReadAsync(Guid tenantId, bool canViewAll)
    {
        await using var db = NewContext(tenantId);
        var service = new SignatureWorkReadService(
            db,
            Options.Create(new SignatureVisibilityOptions { Enabled = true })
        );
        return await service.ListAsync(
            new SignatureWorkFilter(tenantId, [_user], _viewer, canViewAll, Window().ExpiredSinceUtc)
        );
    }

    private static SignatureRequest Sent(Guid tenantId, Guid author, Guid? preparer, Guid? customerId = null)
    {
        var request = preparer is { } id
            ? PreparerRequests.DraftWithPreparer(tenantId, author, id)
            : PreparerRequests.Draft(tenantId, author);
        var document = request.Documents.Single();
        var signer = request
            .AddSigner(
                TestSignatureLimits.Default,
                SignerEmail.Create("client@example.test").Value,
                SignerFullName.Create("Client Signer").Value,
                customerId
            )
            .Value;
        Assert.True(
            request
                .PlaceField(
                    TestSignatureLimits.MaxFieldsPerSigner,
                    signer.Id,
                    document.Id,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                    null,
                    true
                )
                .IsSuccess
        );
        request.AttachVerifiedDocument(document.Id, VerifiedDocument.Create(PreparerRequests.Hash('a'), 50).Value);
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        return request;
    }

    private static async Task SeedAsync(Guid tenantId, params SignatureRequest[] requests)
    {
        await using var db = NewContext(tenantId);
        db.SignatureRequests.AddRange(requests);
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
