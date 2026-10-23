using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Queries;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

// Solicitante y responsable ven por sí mismos solo las requests externas; las de cliente piden asignación.
public sealed class FormRequestVisibilityTests
{
    private const string RepositoryNamespace = "TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories.";
    private static readonly Guid Requester = Guid.Parse("40000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Responsible = Guid.Parse("40000000-0000-0000-0000-0000000000a2");
    private static readonly Guid AssignedEmployee = Guid.Parse("40000000-0000-0000-0000-0000000000a3");

    [Fact]
    public async Task Unassigned_employee_who_assigned_a_customer_request_cannot_open_it()
    {
        await using var db = await SeedAsync();
        var request = await db
            .FormRequests.IgnoreQueryFilters()
            .SingleAsync(item => item.ParticipantKind == FormParticipantKind.Customer);

        Assert.False(await Visibility(db).CanViewAsync(TenantId, Requester, false, request));
        Assert.True(await Visibility(db).CanViewAsync(TenantId, AssignedEmployee, false, request));
        Assert.True(await Visibility(db).CanViewAsync(TenantId, Requester, true, request));
    }

    [Fact]
    public async Task Unassigned_employee_who_assigned_a_customer_request_does_not_list_it()
    {
        await using var db = await SeedAsync();

        var listed = await ListAsync(db, Requester);

        Assert.Equal([FormParticipantKind.External], listed.Select(item => item.Participant.Kind).ToArray());
        Assert.Equal(
            [FormParticipantKind.Customer],
            (await ListAsync(db, AssignedEmployee)).Select(item => item.Participant.Kind).ToArray()
        );
    }

    [Theory]
    [InlineData("40000000-0000-0000-0000-0000000000a1")]
    [InlineData("40000000-0000-0000-0000-0000000000a2")]
    public async Task Requester_and_responsible_still_see_an_external_request(string actor)
    {
        await using var db = await SeedAsync();
        var actorId = Guid.Parse(actor);
        var request = await db
            .FormRequests.IgnoreQueryFilters()
            .SingleAsync(item => item.ParticipantKind == FormParticipantKind.External);

        Assert.True(await Visibility(db).CanViewAsync(TenantId, actorId, false, request));
        Assert.Contains(await ListAsync(db, actorId), item => item.Id == request.Id);
    }

    private static async Task<IReadOnlyList<FormRequestListItemResponse>> ListAsync(TasksDbContext db, Guid actorId)
    {
        var page = await StaffReads(db)
            .ListAsync(
                new ListFormRequestsQuery(TenantId, actorId, false, null, null, null, null, null, null, null, 1, 25),
                Now
            );
        return page.Items;
    }

    private static async Task<TasksDbContext> SeedAsync()
    {
        var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );
        db.FormRequests.Add(CustomerRequest(requestedBy: Requester));
        db.FormRequests.Add(ExternalRequest());
        db.CustomerAssignmentProjections.Add(
            CustomerAssignmentProjection.Create(TenantId, CustomerId, AssignedEmployee, Now)
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static FormRequest ExternalRequest() =>
        FormRequest
            .Create(
                TenantId,
                FormParticipantRef.External(Guid.NewGuid()),
                Version(),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                Responsible,
                Requester,
                Now
            )
            .Value;

    private static IFormRequestVisibilityFilter Visibility(TasksDbContext db) =>
        Create<IFormRequestVisibilityFilter>("FormRequestVisibilityFilter", db);

    private static IFormRequestStaffReadRepository StaffReads(TasksDbContext db) =>
        Create<IFormRequestStaffReadRepository>("FormRequestStaffReadRepository", db);

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
