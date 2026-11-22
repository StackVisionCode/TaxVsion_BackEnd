using BuildingBlocks.CustomerVisibility;
using TaxVision.Tasks.ClientForms.Application.Participants.Prospects;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Tests.ClientForms.Integration;

/// <summary>El GROUP BY sobre el estado derivado se ejecuta en SQL Server real, con dos alcances y sin cruzar tenants.</summary>
public sealed class ProspectSummaryIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employee = Guid.NewGuid();
    private readonly Guid _linkedCustomer = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        await using var db = _database.NewContext(Guid.Empty);
        var verified = Participant(_tenantId, "verified@example.com", verified: true);
        var converted = Participant(_tenantId, "converted@example.com", verified: true);
        Assert.True(converted.LinkToCustomer(_linkedCustomer, _employee, Now).IsSuccess);
        var lost = Participant(_tenantId, "lost@example.com", verified: true);
        Assert.True(lost.MarkLost("No", _employee, Now).IsSuccess);
        db.FormParticipants.AddRange(
            Participant(_tenantId, "invited@example.com", verified: false),
            verified,
            converted,
            lost,
            Participant(Guid.NewGuid(), "other-tenant@example.com", verified: false)
        );
        db.CustomerAssignmentProjections.Add(
            CustomerAssignmentProjection.Create(_tenantId, _linkedCustomer, _employee, Now)
        );
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task Counts_by_status_for_the_office_and_for_a_restricted_employee()
    {
        await using var db = _database.NewContext(Guid.Empty);
        var reads = Reads(db);

        var office = await CountProspectsByStatusHandler.Handle(new(_tenantId, _employee, true), reads, default);
        var employee = await CountProspectsByStatusHandler.Handle(new(_tenantId, _employee, false), reads, default);

        Assert.Equal(new ProspectStatusCountsResponse(1, 1, 0, 1, 0, 1), office);
        Assert.Equal(new ProspectStatusCountsResponse(0, 0, 0, 1, 0, 0), employee);
    }

    private static FormParticipant Participant(Guid tenantId, string email, bool verified)
    {
        var participant = FormParticipant
            .Create(tenantId, email, null, null, FormParticipantSource.DirectedRequest, Now)
            .Value;
        if (verified)
            participant.MarkVerified(Now);
        return participant;
    }

    private static IProspectReadRepository Reads(TasksDbContext db) =>
        (IProspectReadRepository)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(
                    "TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories.ProspectReadRepository",
                    throwOnError: true
                )!,
                db
            )!;
}
