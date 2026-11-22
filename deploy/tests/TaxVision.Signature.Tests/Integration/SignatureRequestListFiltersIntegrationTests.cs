using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Queries.List;
using TaxVision.Signature.Application.Requests.Queries.Work;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Queries;
using TaxVision.Signature.Tests.Application.Offboarding;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// Filtros del listado que abre el dashboard, contra SQL Server real: el total del listado es el número del widget
/// (expiringWithin48h del resumen y "waiting" de My work) para el mismo actor y con el mismo recorte.
/// </summary>
public sealed class SignatureRequestListFiltersIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employee = Guid.NewGuid();
    private readonly Guid _author = Guid.NewGuid();
    private readonly Guid _assignedCustomer = Guid.NewGuid();
    private readonly Guid _otherCustomer = Guid.NewGuid();
    private readonly DateTime _now = DateTime.UtcNow;

    public async Task InitializeAsync()
    {
        var mineExpiring = Sent(_employee, null, _assignedCustomer);
        var othersExpiring = Sent(_author, null, _assignedCustomer);
        var hiddenExpiring = Sent(_author, null, _otherCustomer);
        var mineLater = Sent(_employee, null, _assignedCustomer);
        // Autora y preparadora sin firmar: espera su firma (open en My work), no a los clientes.
        var mineAsPreparer = Sent(_employee, _employee, _assignedCustomer);
        var mineHidden = Sent(_employee, null, _otherCustomer);
        var expired = Sent(_employee, null, _assignedCustomer);
        Assert.True(expired.MarkExpired(_now.AddHours(-1)).IsSuccess);
        var draft = PreparerRequests.Draft(_tenantId, _employee);

        await using var db = NewContext();
        db.SignatureRequests.AddRange(
            mineExpiring,
            othersExpiring,
            hiddenExpiring,
            mineLater,
            mineAsPreparer,
            mineHidden,
            expired,
            draft
        );
        db.Set<CustomerAssignmentProjection>()
            .Add(CustomerAssignmentProjection.Create(_tenantId, _assignedCustomer, _employee, _now));
        await db.SaveChangesAsync();

        Guid[] expiring = [mineExpiring.Id, othersExpiring.Id, hiddenExpiring.Id, mineAsPreparer.Id];
        await db
            .SignatureRequests.Where(r => expiring.Contains(r.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAtUtc, _now.AddHours(10)));
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.SignatureRequests.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId)
            .ExecuteDeleteAsync();
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public async Task Expiring_total_matches_expiring_within_48h_of_the_summary(bool canViewAll, int expected)
    {
        var list = await ListAsync(canViewAll, status: null, expiringSoon: true);
        var summary = await SummaryAsync(canViewAll);

        Assert.Equal(expected, summary.ExpiringWithin48h);
        Assert.Equal(summary.ExpiringWithin48h, list.TotalCount);
        Assert.All(list.Items, item => Assert.Equal(SignatureRequestStatus.InProgress, item.Status));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public async Task Mine_in_progress_total_matches_waiting_on_clients_of_my_work(bool canViewAll, int expected)
    {
        var list = await ListAsync(canViewAll, SignatureRequestStatus.InProgress, mineOnly: true);
        var waiting = await WaitingAsync(canViewAll);

        Assert.Equal(expected, waiting);
        Assert.Equal(waiting, list.TotalCount);
    }

    [Fact]
    public async Task Without_the_new_filters_the_list_is_unchanged()
    {
        var all = await ListAsync(canViewAll: true, status: null);
        var inProgress = await ListAsync(canViewAll: true, SignatureRequestStatus.InProgress);

        Assert.Equal(8, all.TotalCount);
        Assert.Equal(6, inProgress.TotalCount);
    }

    // ----- helpers -----

    private async Task<ListSignatureRequestsResult> ListAsync(
        bool canViewAll,
        SignatureRequestStatus? status,
        bool expiringSoon = false,
        bool mineOnly = false
    )
    {
        await using var db = NewContext();
        var service = new SignatureRequestReadService(db, Visibility());
        return await service.ListAsync(
            new ListSignatureRequestsQuery(
                _tenantId,
                status,
                null,
                1,
                100,
                _employee,
                canViewAll,
                ExpiringSoon: expiringSoon,
                MineOnly: mineOnly,
                NowUtc: _now
            )
        );
    }

    // Misma regla de alcance que GetSignatureRequestsSummaryHandler.
    private async Task<SignatureRequestsLiveCounts> SummaryAsync(bool canViewAll)
    {
        await using var db = NewContext();
        return await new SignatureRequestsSummaryReader(db).ReadAsync(
            new SignatureRequestsSummaryFilter(
                _tenantId,
                canViewAll ? null : _employee,
                _now.AddDays(-1),
                _now.AddDays(1),
                _now
            )
        );
    }

    private async Task<int> WaitingAsync(bool canViewAll)
    {
        await using var db = NewContext();
        var window = SignatureWorkWindow.For(_now, ResolvedTimeZone.Utc);
        var rows = await new SignatureWorkReadService(db, Visibility()).ListAsync(
            new SignatureWorkFilter(_tenantId, [_employee], _employee, canViewAll, window.ExpiredSinceUtc)
        );
        return SignatureWork.Summarize(_employee, rows, window).Counts.Single().Waiting;
    }

    private static IOptions<SignatureVisibilityOptions> Visibility() =>
        Options.Create(new SignatureVisibilityOptions { Enabled = true });

    private SignatureRequest Sent(Guid author, Guid? preparer, Guid customerId)
    {
        var request = preparer is { } id
            ? PreparerRequests.DraftWithPreparer(_tenantId, author, id)
            : PreparerRequests.Draft(_tenantId, author);
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
        Assert.True(request.Send(_now).IsSuccess);
        return request;
    }

    private SignatureDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<SignatureDbContext>()
                .UseSqlServer(SignatureTestDatabase.ConnectionString)
                .Options,
            new FixedTenant(_tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
