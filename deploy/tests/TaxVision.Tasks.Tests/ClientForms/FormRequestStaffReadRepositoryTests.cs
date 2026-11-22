using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Queries;
using TaxVision.Tasks.ClientForms.Application.Requests.Work;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

// La bandeja filtrada "a mi cargo y esperando al cliente" lista lo mismo que cuenta la fila Forms de My work.
public sealed class FormRequestStaffReadRepositoryTests
{
    private const string RepositoryNamespace = "TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories.";
    private static readonly Guid Mia = Guid.Parse("40000000-0000-0000-0000-0000000000b1");
    private static readonly Guid Leo = Guid.Parse("40000000-0000-0000-0000-0000000000b2");
    private static readonly FormRequestStatus[] Waiting = [FormRequestStatus.Pending, FormRequestStatus.Returned];

    [Theory]
    [InlineData("40000000-0000-0000-0000-0000000000b1", 2)]
    [InlineData("40000000-0000-0000-0000-0000000000b2", 1)]
    public async Task Assigned_to_me_and_waiting_lists_what_my_work_counts(string actor, int expected)
    {
        await using var db = await SeedAsync();
        var actorId = Guid.Parse(actor);

        var listed = await ListAsync(db, actorId, assignedToMe: true);
        var work = await WorkReader(db).CountAsync(TenantId, actorId, actorId, false, Today, default);

        Assert.Equal(work.Waiting, listed.TotalCount);
        Assert.Equal(expected, listed.TotalCount);
    }

    [Fact]
    public async Task Without_the_filter_the_inbox_still_lists_what_the_actor_can_see()
    {
        await using var db = await SeedAsync();

        var listed = await ListAsync(db, Mia, assignedToMe: false);

        Assert.Equal(3, listed.TotalCount);
    }

    private static DateOnly Today => DateOnly.FromDateTime(Now);

    private static Task<BuildingBlocks.Common.PagedResult<FormRequestListItemResponse>> ListAsync(
        TasksDbContext db,
        Guid actorId,
        bool assignedToMe
    ) =>
        Create<IFormRequestStaffReadRepository>("FormRequestStaffReadRepository", db)
            .ListAsync(
                new ListFormRequestsQuery(
                    TenantId,
                    actorId,
                    false,
                    null,
                    null,
                    null,
                    Waiting,
                    null,
                    null,
                    null,
                    1,
                    25,
                    FormRequestInboxView.All,
                    assignedToMe
                ),
                Now
            );

    // Mia: dos de cliente a su cargo (pendiente y devuelta), una en revisión y una externa que pidió para Leo.
    private static async Task<TasksDbContext> SeedAsync()
    {
        var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );
        var version = Version();
        var participant = FormParticipant
            .Create(TenantId, "ada@example.com", "Ada Lovelace", null, FormParticipantSource.DirectedRequest, Now)
            .Value;
        var pending = CustomerRequest(version, requestedBy: Mia);
        var returned = CustomerRequest(version, requestedBy: Mia);
        Submit(returned);
        Assert.True(returned.ReturnForChanges("Fix it", [], Mia, Now.AddMinutes(1)).IsSuccess);
        var inReview = CustomerRequest(version, requestedBy: Mia);
        Submit(inReview);
        db.FormTemplateVersions.Add(version);
        db.FormParticipants.Add(participant);
        db.FormRequests.AddRange(pending, returned, inReview, ExternalFor(Leo, version, participant.Id));
        db.CustomerAssignmentProjections.Add(CustomerAssignmentProjection.Create(TenantId, CustomerId, Mia, Now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static FormRequest ExternalFor(
        Guid responsible,
        TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion version,
        Guid participantId
    ) =>
        FormRequest
            .Create(
                TenantId,
                FormParticipantRef.External(participantId),
                version,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                responsible,
                Mia,
                Now
            )
            .Value;

    private static IFormRequestWorkReader WorkReader(TasksDbContext db) =>
        Create<IFormRequestWorkReader>("FormRequestWorkReader", db);

    // Las implementaciones son internas; se instancian por su contrato público.
    private static T Create<T>(string typeName, TasksDbContext db) =>
        (T)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(RepositoryNamespace + typeName, throwOnError: true)!,
                db
            )!;

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
