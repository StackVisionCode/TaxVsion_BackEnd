using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests.Queries;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ConsentRuntimeTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid CustomerId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
    private const string NoticeBody = "{\"type\":\"doc\",\"content\":[]}";

    [Fact]
    public async Task Accept_uses_server_snapshot_and_is_idempotent()
    {
        var fixture = Fixture("beforeCollection");

        var first = await Accept(fixture);
        var second = await Accept(fixture);

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value.RecordId, second.Value.RecordId);
        var record = Assert.Single(fixture.Records.Items);
        Assert.Equal(fixture.NoticeVersion.ContentHash, record.NoticeContentHash);
        Assert.Equal(fixture.NoticeVersion.TextSnapshot, record.TextSnapshot);
        Assert.Equal(NoticeBody, record.NoticeBodyJson);
        Assert.Equal("I agree to this notice.", record.AcceptanceText);
        Assert.Equal(["consent.accepted"], fixture.Audit.Actions);
    }

    [Fact]
    public async Task Evidence_of_another_participant_is_never_reused()
    {
        var fixture = Fixture("beforeCollection");
        var participantId = Guid.NewGuid();
        fixture.Records.Add(
            FormConsentRecord
                .Create(
                    TenantId,
                    fixture.Request.Id,
                    "fd_consent01",
                    FormParticipantKind.External,
                    null,
                    participantId,
                    FormActor.ExternalSession(participantId, Guid.NewGuid()),
                    fixture.NoticeVersion,
                    "I agree to this notice.",
                    FormConsentOutcome.Accepted,
                    "127.0.0.1",
                    "tests",
                    Now
                )
                .Value
        );

        var result = await Accept(fixture);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, fixture.Records.Items.Count);
        Assert.Equal(CustomerId, fixture.Records.Items.Single(item => item.Id == result.Value.RecordId).CustomerId);
    }

    [Fact]
    public async Task Accept_is_rejected_once_the_request_no_longer_takes_answers()
    {
        var fixture = Fixture("beforeSubmit");
        fixture.Request.Submit(new ProtectedAnswers("{}", 1), [], [], fixture.Context.Actor, Now);

        var result = await Accept(fixture);

        Assert.Equal(FormRequestErrors.NotEditable, result.Error);
        Assert.Empty(fixture.Records.Items);
    }

    [Fact]
    public async Task Concurrent_accept_returns_the_record_that_won_the_unique_index()
    {
        var fixture = Fixture("beforeCollection");
        var winner = Accepted(fixture);
        var records = new RacingConsentRecordRepository(winner);

        var result = await AcceptFormConsentHandler.Handle(
            Command(fixture),
            fixture.Requests,
            fixture.Versions,
            fixture.Notices,
            records,
            new PlainProtector(),
            fixture.Engine,
            fixture.Audit,
            new ConflictingUnitOfWork(1, "Persistence.UniqueConstraint"),
            new RecordingClientFormsMetrics(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(winner.Id, result.Value.RecordId);
        Assert.Equal(1, records.Discarded);
        Assert.Empty(fixture.Audit.Actions);
    }

    [Fact]
    public async Task Before_collection_blocks_personal_answers_until_accepted()
    {
        var fixture = Fixture("beforeCollection");
        var definition = fixture.Engine.ParseDefinition(fixture.Version.Definition.Json).Value;
        var answers = fixture
            .Engine.ParseAnswers("{\"schemaVersion\":1,\"values\":{\"fd_personal1\":\"Alice\"}}")
            .Value;
        var gate = new FormConsentGate(fixture.Notices, fixture.Records);

        var blocked = await gate.EnsureBeforeCollectionAsync(
            fixture.Context,
            fixture.Request,
            definition,
            answers,
            false,
            default
        );
        fixture.Records.Add(Accepted(fixture));
        var allowed = await gate.EnsureBeforeCollectionAsync(
            fixture.Context,
            fixture.Request,
            definition,
            answers,
            false,
            default
        );

        Assert.Equal(ConsentErrors.RequiredBeforeCollection, blocked.Error);
        Assert.True(allowed.IsSuccess);
    }

    [Fact]
    public async Task Before_submit_requires_visible_blocking_acceptance()
    {
        var fixture = Fixture("beforeSubmit");
        var definition = fixture.Engine.ParseDefinition(fixture.Version.Definition.Json).Value;
        var answers = fixture.Engine.ParseAnswers("{\"schemaVersion\":1,\"values\":{}}").Value;
        var visibility = fixture.Engine.EvaluateVisibility(definition, answers);
        var gate = new FormConsentGate(fixture.Notices, fixture.Records);

        var blocked = await gate.EnsureBeforeSubmitAsync(
            fixture.Context,
            fixture.Request,
            definition,
            visibility,
            default
        );
        fixture.Records.Add(Accepted(fixture));
        var allowed = await gate.EnsureBeforeSubmitAsync(
            fixture.Context,
            fixture.Request,
            definition,
            visibility,
            default
        );

        Assert.Equal(ConsentErrors.RequiredBeforeSubmit, blocked.Error);
        Assert.True(allowed.IsSuccess);
    }

    [Fact]
    public async Task Decline_is_recorded_and_keeps_collection_blocked()
    {
        var fixture = Fixture("beforeCollection");
        var definition = fixture.Engine.ParseDefinition(fixture.Version.Definition.Json).Value;
        var answers = fixture
            .Engine.ParseAnswers("{\"schemaVersion\":1,\"values\":{\"fd_personal1\":\"Alice\"}}")
            .Value;

        var declined = await Decline(fixture);
        var again = await Decline(fixture);
        var gate = await new FormConsentGate(fixture.Notices, fixture.Records).EnsureBeforeCollectionAsync(
            fixture.Context,
            fixture.Request,
            definition,
            answers,
            false,
            default
        );

        Assert.Equal(declined.Value.RecordId, again.Value.RecordId);
        var record = Assert.Single(fixture.Records.Items);
        Assert.Equal(FormConsentOutcome.Declined, record.Outcome);
        Assert.Null(record.AcceptanceText);
        Assert.Equal(FormRequestStatus.Pending, fixture.Request.Status);
        Assert.Equal(ConsentErrors.RequiredBeforeCollection, gate.Error);
        Assert.Equal(["consent.declined"], fixture.Audit.Actions);
    }

    [Fact]
    public async Task Decline_after_accepting_is_left_to_the_office()
    {
        var fixture = Fixture("beforeCollection");
        await Accept(fixture);

        var result = await Decline(fixture);

        Assert.Equal(ConsentErrors.AlreadyAccepted, result.Error);
        Assert.Single(fixture.Records.Items);
    }

    [Fact]
    public async Task Submit_binds_every_pending_record_of_the_participant()
    {
        var fixture = Fixture("beforeSubmit");
        fixture.Request.SaveDraft(
            new ProtectedAnswers("{\"schemaVersion\":1,\"values\":{\"fd_personal1\":\"Alice\"}}", 1),
            50,
            fixture.Context.Actor,
            Now
        );
        var accepted = Accepted(fixture);
        var presented = Record(fixture, "entry_notice", FormConsentOutcome.Presented);
        var otherId = Guid.NewGuid();
        var other = FormConsentRecord
            .Create(
                TenantId,
                fixture.Request.Id,
                "fd_consent01",
                FormParticipantKind.External,
                null,
                otherId,
                FormActor.ExternalSession(otherId, Guid.NewGuid()),
                fixture.NoticeVersion,
                null,
                FormConsentOutcome.Presented,
                "127.0.0.1",
                "tests",
                Now
            )
            .Value;
        fixture.Records.Add(accepted);
        fixture.Records.Add(presented);
        fixture.Records.Add(other);

        var result = await SubmitPortalFormRequestHandler.Handle(
            new SubmitPortalFormRequestCommand(fixture.Context, fixture.Request.Id, [], Now),
            fixture.Requests,
            fixture.Versions,
            fixture.Engine,
            new PlainProtector(),
            new ActiveCustomers(),
            new NoIntakeLinks(),
            new FormConsentGate(fixture.Notices, fixture.Records),
            fixture.Records,
            FormActionTestDoubles.Planner(fixture.Versions, fixture.Engine),
            Options.Create(new ClientFormsOptions()),
            fixture.Audit,
            fixture.UnitOfWork,
            new FakeMessageBus(),
            new FixedCorrelation(),
            new RecordingClientFormsMetrics(),
            default
        );

        Assert.True(result.Value.Submitted);
        Assert.Equal(1, accepted.RevisionNumber);
        Assert.Equal(1, presented.RevisionNumber);
        Assert.Null(other.RevisionNumber);
    }

    // E2E F8: el portal solo escribe en la respuesta los archivos disponibles, así que uno en revisión no aparecía en
    // ella y el servidor dejaba enviar el formulario.
    [Fact]
    public async Task Submit_is_blocked_while_an_uploaded_file_is_under_review_even_if_the_answer_omits_it()
    {
        var fixture = Fixture("beforeSubmit");
        fixture.Request.SaveDraft(
            new ProtectedAnswers("{\"schemaVersion\":1,\"values\":{\"fd_personal1\":\"Alice\"}}", 1),
            50,
            fixture.Context.Actor,
            Now
        );
        fixture.Records.Add(Accepted(fixture));
        var fileId = Guid.NewGuid();
        fixture.Request.RegisterAttachment(new FormAnswerAddress("fd_upload001", null), fileId, "id.png", "image/png", 2048, 5, 20, null, Now);
        fixture.Request.ApplyFileVerdict(fileId, new FormFileVerdict(FormFileVerdictStatus.PendingReview, null, null, null, Now), Now);

        var result = await SubmitPortalFormRequestHandler.Handle(
            new SubmitPortalFormRequestCommand(fixture.Context, fixture.Request.Id, [], Now),
            fixture.Requests,
            fixture.Versions,
            fixture.Engine,
            new PlainProtector(),
            new ActiveCustomers(),
            new NoIntakeLinks(),
            new FormConsentGate(fixture.Notices, fixture.Records),
            fixture.Records,
            FormActionTestDoubles.Planner(fixture.Versions, fixture.Engine),
            Options.Create(new ClientFormsOptions()),
            fixture.Audit,
            fixture.UnitOfWork,
            new FakeMessageBus(),
            new FixedCorrelation(),
            new RecordingClientFormsMetrics(),
            default
        );

        Assert.False(result.Value.Submitted);
        Assert.Contains(result.Value.Issues, issue => issue.Code == "answer.attachment_under_review" && issue.ComponentId == "fd_upload001");
    }

    [Fact]
    public async Task Revision_shows_only_evidence_bound_up_to_that_revision()
    {
        var fixture = Fixture("beforeSubmit");
        fixture.Request.Submit(new ProtectedAnswers("{}", 1), [], [], fixture.Context.Actor, Now);
        fixture.Request.ReturnForChanges("Please review.", [], UserId, Now);
        fixture.Request.Submit(new ProtectedAnswers("{}", 1), [], [], fixture.Context.Actor, Now.AddMinutes(5));
        var first = Accepted(fixture);
        var second = Record(fixture, "entry_notice", FormConsentOutcome.Presented);
        var pending = Record(fixture, "fd_consent01", FormConsentOutcome.Declined);
        first.BindToRevision(1);
        second.BindToRevision(2);
        fixture.Records.Add(first);
        fixture.Records.Add(second);
        fixture.Records.Add(pending);

        var revisionOne = await Revision(fixture, 1);
        var revisionTwo = await Revision(fixture, 2);

        Assert.Equal([first.Id], revisionOne.Value.Consents.Select(item => item.RecordId));
        Assert.Equal([first.Id, second.Id], revisionTwo.Value.Consents.Select(item => item.RecordId));
    }

    private static Task<Result<AcceptFormConsentResponse>> Accept(FixtureState fixture) =>
        AcceptFormConsentHandler.Handle(
            Command(fixture),
            fixture.Requests,
            fixture.Versions,
            fixture.Notices,
            fixture.Records,
            new PlainProtector(),
            fixture.Engine,
            fixture.Audit,
            fixture.UnitOfWork,
            new RecordingClientFormsMetrics(),
            default
        );

    private static Task<Result<DeclineFormConsentResponse>> Decline(FixtureState fixture) =>
        DeclineFormConsentHandler.Handle(
            new DeclineFormConsentCommand(
                fixture.Context,
                fixture.Request.Id,
                "fd_consent01",
                "127.0.0.1",
                "tests",
                Now
            ),
            fixture.Requests,
            fixture.Versions,
            fixture.Notices,
            fixture.Records,
            new PlainProtector(),
            fixture.Engine,
            fixture.Audit,
            fixture.UnitOfWork,
            new RecordingClientFormsMetrics(),
            default
        );

    private static Task<Result<FormRequestRevisionResponse>> Revision(FixtureState fixture, int number) =>
        GetFormRequestRevisionHandler.Handle(
            new GetFormRequestRevisionQuery(TenantId, UserId, true, fixture.Request.Id, number),
            fixture.Requests,
            new AllowAllVisibility(),
            new PlainProtector(),
            fixture.Records,
            fixture.Audit,
            fixture.UnitOfWork,
            default
        );

    private static AcceptFormConsentCommand Command(FixtureState fixture) =>
        new(fixture.Context, fixture.Request.Id, "fd_consent01", "127.0.0.1", "tests", Now);

    private static FormConsentRecord Accepted(FixtureState fixture) =>
        FormConsentRecord
            .Create(
                TenantId,
                fixture.Request.Id,
                "fd_consent01",
                FormParticipantKind.Customer,
                CustomerId,
                null,
                fixture.Context.Actor,
                fixture.NoticeVersion,
                "I agree to this notice.",
                FormConsentOutcome.Accepted,
                "127.0.0.1",
                "tests",
                Now
            )
            .Value;

    private static FormConsentRecord Record(FixtureState fixture, string componentId, FormConsentOutcome outcome) =>
        FormConsentRecord
            .Create(
                TenantId,
                fixture.Request.Id,
                componentId,
                FormParticipantKind.Customer,
                CustomerId,
                null,
                fixture.Context.Actor,
                fixture.NoticeVersion,
                null,
                outcome,
                "127.0.0.1",
                "tests",
                Now
            )
            .Value;

    private static FixtureState Fixture(string timing)
    {
        var notice = ConsentNotice
            .Create(TenantId, "privacy_notice", "Privacy notice", ConsentNoticePurpose.General, UserId, Now)
            .Value;
        notice.Publish("en", NoticeBody, "Exact server notice", new string('b', 64), UserId, Now);
        var version = Version(timing);
        var request = FormRequest
            .Create(
                TenantId,
                FormParticipantRef.Customer(CustomerId),
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
                null,
                UserId,
                Now
            )
            .Value;
        return new FixtureState(
            request,
            version,
            notice.Versions.Single(),
            ParticipantContext.Portal(TenantId, CustomerId, UserId),
            new MemoryRequestRepository(request),
            new MemoryFormTemplateVersionRepository(version),
            new MemoryConsentNoticeRepository(notice),
            new MemoryFormConsentRecordRepository(),
            new FormEngine(new DataCollectionLimits()),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork()
        );
    }

    private static FormTemplateVersion Version(string timing) =>
        FormTemplateVersion.Create(
            TenantId,
            Guid.NewGuid(),
            1,
            "Consent form",
            FormPurpose.ClientIntake,
            FormPresentation.Wizard,
            ClientFormsTestDocuments.Definition(
                """
                {"schemaVersion":1,"title":"Consent form","purpose":"clientIntake","presentation":"wizard","settings":{"showProgress":true,"collectionBasis":"consent"},"pages":[{"id":"pg_abcdefgh","title":"Page","description":null,"visibleWhen":null,"style":null,"sections":[{"id":"sc_abcdefgh","title":"Section","description":null,"visibleWhen":null,"repeat":null,"style":null,"components":[{"id":"fd_consent01","type":"consent","key":null,"label":null,"help":null,"required":false,"requiredWhen":null,"visibleWhen":null,"dataClass":null,"reportable":false,"prefill":null,"style":null,"props":{"noticeKey":"privacy_notice","noticeVersionNumber":1,"mode":"acceptance","timing":"$TIMING$","acceptanceText":"I agree to this notice.","blocking":true}},{"id":"fd_personal1","type":"text","key":null,"label":"Name","help":null,"required":false,"requiredWhen":null,"visibleWhen":null,"dataClass":"personal","reportable":false,"prefill":null,"style":null,"props":{}}]}]}],"mappings":[],"actions":[]}
                """.Replace("$TIMING$", timing, StringComparison.Ordinal)
            ),
            ClientFormsTestDocuments.Hash(new string('c', 64)),
            2,
            UserId,
            Now
        );

    private sealed record FixtureState(
        FormRequest Request,
        FormTemplateVersion Version,
        ConsentNoticeVersion NoticeVersion,
        ParticipantContext Context,
        MemoryRequestRepository Requests,
        MemoryFormTemplateVersionRepository Versions,
        MemoryConsentNoticeRepository Notices,
        MemoryFormConsentRecordRepository Records,
        IFormEngine Engine,
        RecordingFormAuditLog Audit,
        RecordingUnitOfWork UnitOfWork
    );

    // Simula la carrera: la lectura previa no ve nada y el índice único rechaza el guardado.
    private sealed class RacingConsentRecordRepository(FormConsentRecord winner) : IFormConsentRecordRepository
    {
        private int _reads;

        internal int Discarded { get; private set; }

        public Task<FormConsentRecord?> GetAsync(FormConsentRecordKey key, CancellationToken ct = default) =>
            Task.FromResult(++_reads == 1 ? null : winner);

        public Task<IReadOnlyList<FormConsentRecord>> ListForRequestAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<FormConsentRecord>>([winner]);

        public void Add(FormConsentRecord record) { }

        public void Discard(FormConsentRecord record) => Discarded++;
    }

    private sealed class PlainProtector : IAnswersProtector
    {
        public ProtectedAnswers Protect(string rawJson) => new(rawJson, 1);

        public string Unprotect(ProtectedAnswers protectedAnswers) => protectedAnswers.Cipher;
    }

    private sealed class AllowAllVisibility : IFormRequestVisibilityFilter
    {
        public Task<bool> CanViewCustomerAsync(
            Guid tenantId,
            Guid actorUserId,
            bool canViewAll,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(true);

        public Task<bool> CanViewAsync(
            Guid tenantId,
            Guid actorUserId,
            bool canViewAll,
            FormRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(true);
    }

    private sealed class ActiveCustomers : IClientFormsCustomerReader
    {
        public Task<ClientFormsCustomerRecord?> GetAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<ClientFormsCustomerRecord?>(new(customerId, true, "Alice"));
    }

    private sealed class NoIntakeLinks : IFormIntakeLinkRepository
    {
        public Task<FormIntakeLink?> GetAsync(Guid tenantId, Guid linkId, CancellationToken ct = default) =>
            Task.FromResult<FormIntakeLink?>(null);

        public Task<FormIntakeLink?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult<FormIntakeLink?>(null);

        public Task<IReadOnlyList<FormIntakeLink>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FormIntakeLink>>([]);

        public void Add(FormIntakeLink link) { }
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public string CorrelationId => "tests";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new MemoryStream();
    }

    private sealed class MemoryRequestRepository(FormRequest request) : IFormRequestRepository
    {
        public void Add(FormRequest item) { }

        public Task<Result<FormRequest>> GetForCommandAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                tenantId == request.TenantId && requestId == request.Id
                    ? Result.Success(request)
                    : Result.Failure<FormRequest>(FormRequestErrors.NotFound)
            );

        public Task<Result<FormRequest>> GetForReviewAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        ) => GetForCommandAsync(tenantId, requestId, ct);

        public Task<bool> HasOpenRequestAsync(
            Guid tenantId,
            Guid customerId,
            Guid templateId,
            Guid? exceptRequestId = null,
            CancellationToken ct = default
        ) => Task.FromResult(false);

        public Task<bool> HasOpenExternalRequestAsync(
            Guid tenantId,
            Guid participantId,
            Guid templateId,
            CancellationToken ct = default
        ) => Task.FromResult(false);

        public Task<IReadOnlyList<FormRequest>> ListOpenByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);

        public Task<IReadOnlyList<FormRequest>> ListByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            bool onlyOpen,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);

        public Task<IReadOnlyList<FormRequest>> ListByAttachmentFileIdAsync(
            Guid fileId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);
    }
}
