using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Queries;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Domain.Projections;
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
    private static readonly Guid ExistingCustomer = Guid.Parse("30000000-0000-0000-0000-0000000000a4");

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

    [Fact]
    public async Task External_inbox_masks_email_and_returns_only_latest_action_attempts()
    {
        await using var db = await SeedAsync();

        var listed = await ListAsync(db, Responsible, FormRequestInboxView.External);

        var item = Assert.Single(listed);
        Assert.Equal("Ada Lovelace", item.DisplayName);
        Assert.Equal("a***@example.com", item.MaskedEmail);
        Assert.Equal("Documents", item.TemplateName);
        Assert.All(item.ActionRuns, run => Assert.True(run.IsLatestAttempt));
    }

    [Fact]
    public async Task Detail_label_matches_the_list_row_and_masks_the_external_email()
    {
        await using var db = await SeedAsync();
        var external = await db
            .FormRequests.IgnoreQueryFilters()
            .SingleAsync(item => item.ParticipantKind == FormParticipantKind.External);

        var label = await StaffReads(db).GetParticipantLabelAsync(TenantId, external);

        var row = Assert.Single(await ListAsync(db, Responsible, FormRequestInboxView.External));
        Assert.Equal(new FormRequestParticipantLabel(row.DisplayName, row.MaskedEmail), label);
        Assert.Equal(new FormRequestParticipantLabel("Ada Lovelace", "a***@example.com"), label);
    }

    [Fact]
    public async Task Detail_label_of_a_customer_request_uses_the_directory_name_without_email()
    {
        await using var db = await SeedAsync();
        db.CustomerDirectoryEntries.Add(
            CustomerDirectoryEntry.Create(TenantId, CustomerId, "Acme LLC", CustomerDirectoryStatus.Active, Now)
        );
        await db.SaveChangesAsync();
        var customer = await db
            .FormRequests.IgnoreQueryFilters()
            .SingleAsync(item => item.ParticipantKind == FormParticipantKind.Customer);

        var label = await StaffReads(db).GetParticipantLabelAsync(TenantId, customer);

        Assert.Equal(new FormRequestParticipantLabel("Acme LLC", null), label);
    }

    [Fact]
    public async Task Staff_detail_returns_the_list_label_with_the_external_email_masked()
    {
        await using var db = await SeedAsync();
        var external = await db
            .FormRequests.IgnoreQueryFilters()
            .SingleAsync(item => item.ParticipantKind == FormParticipantKind.External);

        var detail = await DetailAsync(db, Responsible, external.Id);

        var row = Assert.Single(await ListAsync(db, Responsible, FormRequestInboxView.External));
        Assert.True(detail.IsSuccess);
        Assert.Equal(row.DisplayName, detail.Value.DisplayName);
        Assert.Equal(row.MaskedEmail, detail.Value.MaskedEmail);
        Assert.Equal("a***@example.com", detail.Value.MaskedEmail);
    }

    [Fact]
    public async Task Staff_detail_of_a_customer_request_follows_abac_and_never_returns_an_email()
    {
        await using var db = await SeedAsync();
        var customer = await db
            .FormRequests.IgnoreQueryFilters()
            .SingleAsync(item => item.ParticipantKind == FormParticipantKind.Customer);

        var assigned = await DetailAsync(db, AssignedEmployee, customer.Id);
        var unassigned = await DetailAsync(db, Requester, customer.Id);

        Assert.Equal("Customer", assigned.Value.DisplayName);
        Assert.Null(assigned.Value.MaskedEmail);
        Assert.True(unassigned.IsFailure);
        Assert.Equal(FormRequestErrors.NotFound, unassigned.Error);
    }

    [Fact]
    public async Task Needs_action_includes_failed_runs_but_not_pending_requests()
    {
        await using var db = await SeedAsync();

        var listed = await ListAsync(db, Responsible, FormRequestInboxView.NeedsAction);

        Assert.Equal([FormParticipantKind.External], listed.Select(item => item.Participant.Kind).ToArray());
    }

    [Fact]
    public async Task Needs_action_keeps_retryable_failures_and_leaves_out_cancelled_or_linked()
    {
        await using var db = NewContext();
        var version = Version();
        db.FormTemplateVersions.Add(version);
        var waiting = SubmittedExternal(db, version);
        var completed = SubmittedExternal(db, version);
        Assert.True(completed.Complete(null, Responsible, Now.AddMinutes(1)).IsSuccess);
        var completedWithFailure = SubmittedExternal(db, version, failedRun: true);
        Assert.True(completedWithFailure.Complete(null, Responsible, Now.AddMinutes(1)).IsSuccess);
        var archived = SubmittedExternal(db, version, failedRun: true);
        Assert.True(archived.Archive("Spam", Responsible, Now.AddMinutes(1)).IsSuccess);
        var linkedDuplicate = SubmittedExternal(db, version, duplicateRun: true);
        Assert.True(linkedDuplicate.ReturnForChanges("Fix it", [], Responsible, Now.AddMinutes(1)).IsSuccess);
        Assert.True(linkedDuplicate.TryAttachCustomer(ExistingCustomer, false, null, Now.AddMinutes(2)).IsSuccess);
        var unlinkedDuplicate = SubmittedExternal(db, version, duplicateRun: true);
        Assert.True(unlinkedDuplicate.ReturnForChanges("Fix it", [], Responsible, Now.AddMinutes(1)).IsSuccess);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var listed = await ListAsync(db, Responsible, FormRequestInboxView.NeedsAction);

        Assert.Equal(
            new[] { waiting.Id, completedWithFailure.Id, unlinkedDuplicate.Id }.Order().ToArray(),
            listed.Select(item => item.Id).Order().ToArray()
        );
    }

    private static FormRequest SubmittedExternal(
        TasksDbContext db,
        TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion version,
        bool failedRun = false,
        bool duplicateRun = false
    )
    {
        var participant = FormParticipant
            .Create(
                TenantId,
                $"{Guid.NewGuid():N}@example.com",
                "Guest",
                null,
                FormParticipantSource.DirectedRequest,
                Now
            )
            .Value;
        var request = ExternalRequest(version, participant.Id);
        request.Submit(
            new ProtectedAnswers("{\"schemaVersion\":1,\"values\":{}}", 1),
            [],
            [],
            FormActor.ExternalSession(participant.Id, Guid.NewGuid()),
            Now
        );
        if (failedRun)
            request.CloseActionWithoutCommand(
                "create_client",
                "CreateCustomer",
                FormActionRunStatus.Failed,
                "Customer.Unavailable",
                Now
            );
        if (duplicateRun)
        {
            var command = Guid.NewGuid();
            request.RequestAction("create_client", "CreateCustomer", command, Now);
            request.RejectAction(command, FormActionRunErrors.DuplicateFoundCode, ExistingCustomer, "Email", Now);
        }
        db.FormParticipants.Add(participant);
        db.FormRequests.Add(request);
        return request;
    }

    [Fact]
    public async Task Existing_customer_from_duplicate_is_hidden_without_customer_abac()
    {
        await using var db = await SeedAsync();

        var staff = Assert.Single(await ListAsync(db, Responsible, FormRequestInboxView.External));
        var admin = Assert.Single(await ListAsync(db, Responsible, FormRequestInboxView.External, true));

        Assert.DoesNotContain(staff.ActionRuns, run => run.ExistingCustomerId.HasValue);
        Assert.Contains(admin.ActionRuns, run => run.ExistingCustomerId == ExistingCustomer);
    }

    private static async Task<IReadOnlyList<FormRequestListItemResponse>> ListAsync(
        TasksDbContext db,
        Guid actorId,
        FormRequestInboxView view = FormRequestInboxView.All,
        bool canViewAll = false
    )
    {
        var page = await StaffReads(db)
            .ListAsync(
                new ListFormRequestsQuery(
                    TenantId,
                    actorId,
                    canViewAll,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    1,
                    25,
                    view
                ),
                Now
            );
        return page.Items;
    }

    private static TasksDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );

    private static async Task<TasksDbContext> SeedAsync()
    {
        var db = NewContext();
        var version = Version();
        var participant = FormParticipant
            .Create(TenantId, "ada@example.com", "Ada Lovelace", null, FormParticipantSource.DirectedRequest, Now)
            .Value;
        participant.MarkVerified(Now);
        var external = ExternalRequest(version, participant.Id);
        external.Submit(
            new ProtectedAnswers("{\"schemaVersion\":1,\"values\":{}}", 1),
            [],
            [],
            FormActor.ExternalSession(participant.Id, Guid.NewGuid()),
            Now
        );
        external.CloseActionWithoutCommand(
            "create_client",
            "CreateCustomer",
            FormActionRunStatus.Failed,
            "Customer.Unavailable",
            Now
        );
        var duplicateCommand = Guid.NewGuid();
        external.RequestAction("match_client", "CreateCustomer", duplicateCommand, Now);
        external.RejectAction(duplicateCommand, FormActionRunErrors.DuplicateFoundCode, ExistingCustomer, "Email", Now);
        db.FormTemplateVersions.Add(version);
        db.FormParticipants.Add(participant);
        db.FormRequests.Add(CustomerRequest(version, requestedBy: Requester));
        db.FormRequests.Add(external);
        db.CustomerAssignmentProjections.Add(
            CustomerAssignmentProjection.Create(TenantId, CustomerId, AssignedEmployee, Now)
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static FormRequest ExternalRequest(
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
                Responsible,
                Requester,
                Now
            )
            .Value;

    // Detalle de staff con los repositorios reales sobre la base en memoria.
    private static Task<Result<StaffFormRequestDetailResponse>> DetailAsync(
        TasksDbContext db,
        Guid actorId,
        Guid requestId
    ) =>
        GetFormRequestHandler.Handle(
            new GetFormRequestQuery(TenantId, actorId, false, requestId),
            Create<IFormRequestRepository>("FormRequestRepository", db),
            StaffReads(db),
            Visibility(db),
            Create<IFormTemplateVersionRepository>("FormTemplateVersionRepository", db),
            new PlainFormAnswersProtector(),
            Create<IFormConsentRecordRepository>("FormConsentRecordRepository", db),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            default
        );

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
