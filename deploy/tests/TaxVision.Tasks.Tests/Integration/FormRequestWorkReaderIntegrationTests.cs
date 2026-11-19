using BuildingBlocks.Contracts.Work;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Requests.Work;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Tests.ClientForms;
using TaxVision.Tasks.Tests.ClientForms.Integration;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// Form requests a cargo de una persona (responsable o, sin él, quien la pidió), con la visibilidad de la bandeja
/// para el que mira, traducidas por SQL Server.
/// </summary>
public sealed class FormRequestWorkReaderIntegrationTests : IAsyncLifetime
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");
    private static readonly DateOnly Today = new(2026, 11, 18);

    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _ana = Guid.NewGuid();
    private readonly Guid _luis = Guid.NewGuid();
    private readonly Guid _boss = Guid.NewGuid();
    private readonly Guid _bossClient = Guid.NewGuid();
    private readonly Guid _otherClient = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        await using var db = _database.NewContext(Guid.Empty);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE FormRequests NOCHECK CONSTRAINT ALL;");
        db.FormRequests.AddRange(
            Request(_tenantId, FormParticipantRef.Customer(_bossClient), null, _ana, Today.AddDays(-8)),
            Request(_tenantId, FormParticipantRef.External(Guid.NewGuid()), _ana, _luis, Today.AddDays(1)),
            Request(_tenantId, FormParticipantRef.Customer(_otherClient), _luis, _ana, null),
            Request(_otherTenantId, FormParticipantRef.Customer(_bossClient), _ana, _ana, Today.AddDays(-2))
        );
        db.Set<CustomerAssignmentProjection>()
            .Add(CustomerAssignmentProjection.Create(_tenantId, _bossClient, _boss, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task Counts_what_the_person_is_responsible_for()
    {
        await using var db = _database.NewContext(Guid.Empty);

        var count = await Reader(db).CountAsync(_tenantId, _ana, _ana, true, Today, default);

        Assert.Equal(new WorkCount(WorkKinds.FormRequests, 2, 1, 1, 2), count);
    }

    [Fact]
    public async Task A_restricted_viewer_only_counts_and_lists_the_inbox_he_can_see()
    {
        await using var db = _database.NewContext(Guid.Empty);
        var reader = Reader(db);

        var count = await reader.CountAsync(_tenantId, _ana, _boss, false, Today, default);
        var top = await reader.TopAsync(_tenantId, _ana, _boss, false, WorkLimits.TopItems, default);

        Assert.Equal(new WorkCount(WorkKinds.FormRequests, 1, 1, 0, 1), count);
        var item = Assert.Single(top);
        Assert.Equal(_bossClient, item.CustomerId);
        Assert.Equal(Today.AddDays(-8), item.DueDate);
        Assert.Equal($"/client-forms?formRequestId={item.Id}", item.Link);
    }

    [Fact]
    public async Task Batch_groups_by_person_and_never_crosses_tenants()
    {
        await using var db = _database.NewContext(Guid.Empty);

        var rows = await Reader(db).CountByUserAsync(_tenantId, [_ana, _luis], _boss, true, Today, default);

        Assert.Equal(
            [new WorkBatchRow(_ana, 2, 1), new WorkBatchRow(_luis, 1, 0)],
            rows.OrderBy(r => r.Open).Reverse().ToArray()
        );
    }

    private static IFormRequestWorkReader Reader(TasksDbContext db) =>
        (IFormRequestWorkReader)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(
                    "TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories.FormRequestWorkReader",
                    throwOnError: true
                )!,
                db
            )!;

    private static FormRequest Request(
        Guid tenantId,
        FormParticipantRef participant,
        Guid? responsible,
        Guid requestedBy,
        DateOnly? dueOn
    ) =>
        FormRequest
            .Create(
                tenantId,
                participant,
                FormRequestFixtures.Version(tenantId),
                "Organizer",
                null,
                2026,
                null,
                dueOn is { } day ? LocalDay.EndUtc(day, NewYork) : null,
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                responsible,
                requestedBy,
                new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                dueOn: dueOn
            )
            .Value;
}
