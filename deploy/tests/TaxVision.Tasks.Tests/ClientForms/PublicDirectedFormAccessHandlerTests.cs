using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class PublicDirectedFormAccessHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Challenge_publishes_only_encrypted_payload_with_correlation()
    {
        var fixture = Fixture();
        var codec = new CapturingPayloadCodec();
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork();
        var metrics = new RecordingClientFormsMetrics();

        var result = await RequestPublicDirectedFormChallengeHandler.Handle(
            new RequestPublicDirectedFormChallengeCommand("access-token", "office.test", Now),
            fixture.Grants,
            fixture.Challenges,
            fixture.Requests,
            fixture.Participants,
            fixture.Notices,
            fixture.Security,
            new FixedHostValidator(true),
            new FixedCapabilities(),
            ExternalOptions(),
            codec,
            unitOfWork,
            bus,
            new NoOpCorrelationContext(),
            metrics,
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(["otp:issued"], metrics.Recorded);
        var message = Assert.IsType<FormAccessCodeDeliveryRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal("test", message.CorrelationId);
        Assert.Equal(message.EventId, message.CommandId);
        Assert.Equal("ciphertext", message.Payload.Cipher);
        Assert.DoesNotContain(fixture.Participant.Email, message.Payload.Cipher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456", message.Payload.Cipher, StringComparison.Ordinal);
        Assert.Equal(fixture.Participant.Email, Assert.IsType<FormAccessCodeDeliveryPayload>(codec.Content).Email);
        Assert.Equal(message.ChallengeId, codec.Binding?.ChallengeId);
        Assert.Equal(message.FormRequestId, codec.Binding?.FormRequestId);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task A_failed_challenge_save_publishes_no_code()
    {
        var fixture = Fixture();
        var bus = new FakeMessageBus();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RequestPublicDirectedFormChallengeHandler.Handle(
                new RequestPublicDirectedFormChallengeCommand("access-token", "office.test", Now),
                fixture.Grants,
                fixture.Challenges,
                fixture.Requests,
                fixture.Participants,
                fixture.Notices,
                fixture.Security,
                new FixedHostValidator(true),
                new FixedCapabilities(),
                ExternalOptions(),
                new CapturingPayloadCodec(),
                new FailingUnitOfWork(),
                bus,
                new NoOpCorrelationContext(),
                new RecordingClientFormsMetrics(),
                default
            )
        );

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task A_failed_resend_save_publishes_no_link()
    {
        var fixture = Fixture();
        var bus = new FakeMessageBus();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ResendExternalFormAccessHandler.Handle(
                new ResendExternalFormAccessCommand(TenantId, UserId, true, fixture.Request.Id, Now),
                fixture.Requests,
                new AllowAllVisibility(),
                fixture.Participants,
                fixture.Grants,
                new FixedCapabilities(),
                fixture.Security,
                new CapturingPayloadCodec(),
                new RecordingFormAuditLog(),
                new FailingUnitOfWork(),
                bus,
                new NoOpCorrelationContext(),
                default
            )
        );

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Verify_keeps_the_language_chosen_on_the_public_page()
    {
        var fixture = Fixture();
        await IssueChallengeAsync(fixture);

        var result = await VerifyPublicDirectedFormChallengeHandler.Handle(
            Verify("123456", Now.AddMinutes(1)) with
            {
                Language = "Es",
            },
            fixture.Grants,
            fixture.Challenges,
            fixture.Sessions,
            fixture.Requests,
            fixture.Participants,
            fixture.Notices,
            fixture.ConsentRecords,
            fixture.Security,
            new FixedHostValidator(true),
            new FixedCapabilities(),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            new RecordingClientFormsMetrics(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Es", fixture.Participant.Language);
    }

    [Fact]
    public async Task Challenge_is_a_noop_for_cross_tenant_host()
    {
        var fixture = Fixture();
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await RequestPublicDirectedFormChallengeHandler.Handle(
            new RequestPublicDirectedFormChallengeCommand("access-token", "wrong.test", Now),
            fixture.Grants,
            fixture.Challenges,
            fixture.Requests,
            fixture.Participants,
            fixture.Notices,
            fixture.Security,
            new FixedHostValidator(false),
            new FixedCapabilities(),
            ExternalOptions(),
            new CapturingPayloadCodec(),
            unitOfWork,
            bus,
            new NoOpCorrelationContext(),
            new RecordingClientFormsMetrics(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Fifth_wrong_code_is_persisted_and_blocks_the_challenge()
    {
        var fixture = Fixture();
        await IssueChallengeAsync(fixture);
        var unitOfWork = new RecordingUnitOfWork();
        var metrics = new RecordingClientFormsMetrics();

        for (var attempt = 0; attempt < FormAccessChallenge.MaxAttempts; attempt++)
        {
            var result = await VerifyPublicDirectedFormChallengeHandler.Handle(
                Verify("000000", Now.AddSeconds(attempt)),
                fixture.Grants,
                fixture.Challenges,
                fixture.Sessions,
                fixture.Requests,
                fixture.Participants,
                fixture.Notices,
                fixture.ConsentRecords,
                fixture.Security,
                new FixedHostValidator(true),
                new FixedCapabilities(),
                new RecordingFormAuditLog(),
                unitOfWork,
                metrics,
                default
            );
            Assert.True(result.IsFailure);
            Assert.Equal(PublicDirectedFormAccessErrors.VerificationFailed.Code, result.Error.Code);
        }

        var challenge = Assert.Single(fixture.Challenges.Items);
        Assert.Equal(FormAccessChallenge.MaxAttempts, challenge.Attempts);
        Assert.NotNull(challenge.BlockedUntilUtc);
        Assert.Equal(FormAccessChallenge.MaxAttempts, unitOfWork.Saves);
        Assert.Empty(fixture.Sessions.Items);
        Assert.Equal(
            [.. Enumerable.Repeat("otp:failed", FormAccessChallenge.MaxAttempts - 1), "otp:locked"],
            metrics.Recorded
        );
    }

    [Fact]
    public async Task Valid_code_verifies_participant_and_returns_short_session()
    {
        var fixture = Fixture();
        await IssueChallengeAsync(fixture);
        var unitOfWork = new RecordingUnitOfWork();

        var result = await VerifyPublicDirectedFormChallengeHandler.Handle(
            Verify("123456", Now.AddMinutes(1)),
            fixture.Grants,
            fixture.Challenges,
            fixture.Sessions,
            fixture.Requests,
            fixture.Participants,
            fixture.Notices,
            fixture.ConsentRecords,
            fixture.Security,
            new FixedHostValidator(true),
            new FixedCapabilities(),
            new RecordingFormAuditLog(),
            unitOfWork,
            new RecordingClientFormsMetrics(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("session-token", result.Value.SessionToken);
        Assert.Equal(FormParticipantStatus.Verified, fixture.Participant.Status);
        Assert.NotNull(fixture.Grant.LastUsedAtUtc);
        var session = Assert.Single(fixture.Sessions.Items);
        Assert.Equal(fixture.Request.Id, session.FormRequestId);
        Assert.Equal(fixture.Participant.Id, session.ParticipantId);
        var evidence = Assert.Single(fixture.ConsentRecords.Items);
        Assert.Equal(FormConsentOutcome.Presented, evidence.Outcome);
        Assert.Equal("entry_notice", evidence.ComponentId);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Session_resolution_binds_the_request_and_extends_only_the_sliding_expiry()
    {
        var fixture = Fixture();
        var createdAt = Now.AddMinutes(-20);
        var session = FormAccessSession
            .Create(
                TenantId,
                fixture.Request.Id,
                fixture.Participant.Id,
                "session-hash",
                "127.0.0.1",
                "tests",
                createdAt
            )
            .Value;
        fixture.Sessions.Add(session);
        var unitOfWork = new RecordingUnitOfWork();

        var result = await ResolvePublicFormSessionHandler.Handle(
            new ResolvePublicFormSessionCommand("session-token", "office.test", Now),
            fixture.Sessions,
            fixture.Security,
            new FixedHostValidator(true),
            new FixedCapabilities(),
            unitOfWork,
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.Request.Id, result.Value.BoundRequestId);
        Assert.Equal(fixture.Participant.Id, result.Value.ParticipantId);
        Assert.Equal(session.Id, result.Value.Actor.SessionId);
        Assert.Equal(Now.Add(FormAccessSession.InactivityTimeout), session.ExpiresAtUtc);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Session_from_another_host_is_rejected_without_touching_it()
    {
        var fixture = Fixture();
        var session = FormAccessSession
            .Create(TenantId, fixture.Request.Id, fixture.Participant.Id, "session-hash", "127.0.0.1", "tests", Now)
            .Value;
        fixture.Sessions.Add(session);
        var unitOfWork = new RecordingUnitOfWork();

        var result = await ResolvePublicFormSessionHandler.Handle(
            new ResolvePublicFormSessionCommand("session-token", "wrong.test", Now.AddMinutes(1)),
            fixture.Sessions,
            fixture.Security,
            new FixedHostValidator(false),
            new FixedCapabilities(),
            unitOfWork,
            default
        );

        Assert.True(result.IsFailure);
        Assert.Equal(Now, session.LastSeenAtUtc);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Verify_whose_save_loses_the_race_is_never_a_success_even_with_the_right_code()
    {
        var fixture = Fixture();
        await IssueChallengeAsync(fixture);

        var result = await VerifyAsync(fixture, "123456", Now.AddMinutes(1), new ConflictingUnitOfWork(1));

        Assert.True(result.IsFailure);
        Assert.Equal(PublicDirectedFormAccessErrors.VerificationFailed.Code, result.Error.Code);
    }

    [Fact]
    public async Task Two_verifies_on_the_same_challenge_state_the_second_conflicting_save_is_a_failure()
    {
        var fixture = Fixture();
        await IssueChallengeAsync(fixture);
        var unitOfWork = new ConflictingUnitOfWork(2);

        var first = await VerifyAsync(fixture, "000000", Now.AddMinutes(1), unitOfWork);
        var second = await VerifyAsync(fixture, "123456", Now.AddMinutes(1), unitOfWork);

        Assert.True(first.IsFailure);
        Assert.True(second.IsFailure);
        Assert.Equal(PublicDirectedFormAccessErrors.VerificationFailed.Code, second.Error.Code);
        Assert.Equal(2, unitOfWork.Saves);
    }

    [Fact]
    public async Task Blocked_challenge_refuses_a_new_code_silently_and_verify_keeps_rejecting_until_it_ends()
    {
        var fixture = Fixture();
        await IssueChallengeAsync(fixture);
        for (var attempt = 0; attempt < FormAccessChallenge.MaxAttempts; attempt++)
            await VerifyAsync(fixture, "000000", Now.AddSeconds(attempt), new RecordingUnitOfWork());
        var bus = new FakeMessageBus();

        var reissue = await RequestChallengeAsync(fixture, Now.AddMinutes(2), bus);
        var verify = await VerifyAsync(fixture, "123456", Now.AddMinutes(3), new RecordingUnitOfWork());

        Assert.True(reissue.IsSuccess);
        Assert.Empty(bus.Published);
        Assert.Single(fixture.Challenges.Items);
        Assert.True(verify.IsFailure);
        Assert.Empty(fixture.Sessions.Items);

        var afterBlock = new FakeMessageBus();
        var reissued = await RequestChallengeAsync(
            fixture,
            Now.Add(FormAccessChallenge.BlockDuration).AddMinutes(1),
            afterBlock
        );
        Assert.True(reissued.IsSuccess);
        Assert.Single(afterBlock.Published);
    }

    [Fact]
    public async Task Cancelling_the_request_revokes_its_link_and_open_sessions()
    {
        var fixture = Fixture();
        var session = FormAccessSession
            .Create(TenantId, fixture.Request.Id, fixture.Participant.Id, "session-hash", "127.0.0.1", "tests", Now)
            .Value;
        fixture.Sessions.Add(session);

        var cancelled = await CancelFormRequestHandler.Handle(
            new CancelFormRequestCommand(TenantId, UserId, true, fixture.Request.Id, null, [], Now.AddMinutes(1)),
            fixture.Requests,
            new AllowAllVisibility(),
            fixture.Grants,
            fixture.Sessions,
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            default
        );

        Assert.True(cancelled.IsSuccess);
        Assert.NotNull(fixture.Grant.RevokedAtUtc);
        Assert.NotNull(session.RevokedAtUtc);
        var resolved = await ResolvePublicFormSessionHandler.Handle(
            new ResolvePublicFormSessionCommand("session-token", "office.test", Now.AddMinutes(2)),
            fixture.Sessions,
            fixture.Security,
            new FixedHostValidator(true),
            new FixedCapabilities(),
            new RecordingUnitOfWork(),
            default
        );
        Assert.True(resolved.IsFailure);
        var access = await GetAccessAsync(fixture, Now.AddMinutes(2));
        Assert.Equal(PublicDirectedFormAccessErrors.NotFound.Code, access.Error.Code);
    }

    [Fact]
    public async Task Link_of_a_closed_request_answers_the_generic_not_found()
    {
        var fixture = Fixture();
        Assert.True(fixture.Request.Cancel(null, UserId, Now).IsSuccess);
        var bus = new FakeMessageBus();

        var access = await GetAccessAsync(fixture, Now.AddMinutes(1));
        var challenge = await RequestChallengeAsync(fixture, Now.AddMinutes(1), bus);

        Assert.Equal(PublicDirectedFormAccessErrors.NotFound.Code, access.Error.Code);
        Assert.True(challenge.IsSuccess);
        Assert.Empty(bus.Published);
    }

    private static Task<Result<PublicFormSessionResponse>> VerifyAsync(
        FixtureState fixture,
        string code,
        DateTime nowUtc,
        IUnitOfWork unitOfWork
    ) =>
        VerifyPublicDirectedFormChallengeHandler.Handle(
            Verify(code, nowUtc),
            fixture.Grants,
            fixture.Challenges,
            fixture.Sessions,
            fixture.Requests,
            fixture.Participants,
            fixture.Notices,
            fixture.ConsentRecords,
            fixture.Security,
            new FixedHostValidator(true),
            new FixedCapabilities(),
            new RecordingFormAuditLog(),
            unitOfWork,
            new RecordingClientFormsMetrics(),
            default
        );

    private static Task<Result> RequestChallengeAsync(FixtureState fixture, DateTime nowUtc, FakeMessageBus bus) =>
        RequestPublicDirectedFormChallengeHandler.Handle(
            new RequestPublicDirectedFormChallengeCommand("access-token", "office.test", nowUtc),
            fixture.Grants,
            fixture.Challenges,
            fixture.Requests,
            fixture.Participants,
            fixture.Notices,
            fixture.Security,
            new FixedHostValidator(true),
            new FixedCapabilities(),
            ExternalOptions(),
            new CapturingPayloadCodec(),
            new RecordingUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            new RecordingClientFormsMetrics(),
            default
        );

    private static Task<Result<PublicDirectedFormAccessResponse>> GetAccessAsync(
        FixtureState fixture,
        DateTime nowUtc
    ) =>
        GetPublicDirectedFormAccessHandler.Handle(
            new GetPublicDirectedFormAccessQuery("access-token", "office.test", nowUtc),
            fixture.Grants,
            fixture.Requests,
            fixture.Participants,
            fixture.Security,
            new FixedHostValidator(true),
            new FixedCapabilities(),
            fixture.Notices,
            default
        );

    private static VerifyPublicDirectedFormChallengeCommand Verify(string code, DateTime nowUtc) =>
        new("access-token", "office.test", code, "127.0.0.1", "tests", nowUtc);

    private static IOptions<ClientFormsOptions> ExternalOptions() =>
        Options.Create(new ClientFormsOptions { External = new ClientFormsExternalOptions() });

    private static async Task IssueChallengeAsync(FixtureState fixture)
    {
        var result = await RequestPublicDirectedFormChallengeHandler.Handle(
            new RequestPublicDirectedFormChallengeCommand("access-token", "office.test", Now),
            fixture.Grants,
            fixture.Challenges,
            fixture.Requests,
            fixture.Participants,
            fixture.Notices,
            fixture.Security,
            new FixedHostValidator(true),
            new FixedCapabilities(),
            ExternalOptions(),
            new CapturingPayloadCodec(),
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            new RecordingClientFormsMetrics(),
            default
        );
        Assert.True(result.IsSuccess);
    }

    private static FixtureState Fixture()
    {
        var participant = FormParticipant
            .Create(TenantId, "external@example.com", "External User", null, FormParticipantSource.DirectedRequest, Now)
            .Value;
        var request = Request(participant.Id);
        var notice = FormTemplateHandlerTestDoubles.IdentityNotice(TenantId, UserId, Now);
        var noticeVersion = notice.Versions.Single();
        var grant = FormAccessGrant
            .Create(
                TenantId,
                request.Id,
                notice.Id,
                noticeVersion.VersionNumber,
                "access-hash",
                "oken",
                Now.AddDays(14),
                Now
            )
            .Value;
        return new FixtureState(
            participant,
            request,
            grant,
            new MemoryGrantRepository(grant),
            new MemoryChallengeRepository(),
            new MemorySessionRepository(),
            new ExternalRequestRepository(request),
            new MemoryParticipantRepository(participant),
            new MemoryConsentNoticeRepository(notice),
            new MemoryFormConsentRecordRepository(),
            new FixedSecurity()
        );
    }

    [Fact]
    public async Task Resend_of_an_intake_link_request_reports_that_there_is_no_access_link()
    {
        var fixture = Fixture();
        var intake = Request(fixture.Participant.Id, FormRequestOrigin.IntakeLink);

        var result = await ResendAsync(fixture with { Requests = new ExternalRequestRepository(intake) }, intake.Id);

        Assert.Equal(FormRequestErrors.NoAccessLink, result.Error);
    }

    [Fact]
    public async Task Resend_without_a_grant_reports_that_there_is_no_access_link()
    {
        var fixture = Fixture();

        var result = await ResendAsync(fixture with { Grants = new MemoryGrantRepository() }, fixture.Request.Id);

        Assert.Equal(FormRequestErrors.NoAccessLink, result.Error);
    }

    [Fact]
    public async Task Resend_after_submission_is_rejected_without_spending_an_issue()
    {
        var fixture = Fixture();
        Assert.True(
            fixture
                .Request.Submit(
                    new ProtectedAnswers("{\"schemaVersion\":1,\"values\":{}}", 1),
                    [],
                    [],
                    FormActor.ExternalSession(fixture.Participant.Id, Guid.NewGuid()),
                    Now
                )
                .IsSuccess
        );
        var issued = fixture.Grant.IssueCount;

        var result = await ResendAsync(fixture, fixture.Request.Id);

        Assert.Equal(FormRequestErrors.ResendNotAllowed, result.Error);
        Assert.Equal(issued, fixture.Grant.IssueCount);
    }

    [Fact]
    public async Task Resend_of_a_pending_directed_request_rotates_the_link()
    {
        var fixture = Fixture();
        var issued = fixture.Grant.IssueCount;

        var result = await ResendAsync(fixture, fixture.Request.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(issued + 1, fixture.Grant.IssueCount);
    }

    private static Task<Result> ResendAsync(FixtureState fixture, Guid requestId) =>
        ResendExternalFormAccessHandler.Handle(
            new ResendExternalFormAccessCommand(TenantId, UserId, true, requestId, Now),
            fixture.Requests,
            new AllowAllVisibility(),
            fixture.Participants,
            fixture.Grants,
            new FixedCapabilities(),
            fixture.Security,
            new CapturingPayloadCodec(),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            default
        );

    private static FormRequest Request(Guid participantId, FormRequestOrigin origin = FormRequestOrigin.Directed) =>
        FormRequest
            .Create(
                TenantId,
                FormParticipantRef.External(participantId),
                FormTemplateVersion.Create(
                    TenantId,
                    Guid.NewGuid(),
                    1,
                    "Tax organizer",
                    FormPurpose.Questionnaire,
                    FormPresentation.Wizard,
                    ClientFormsTestDocuments.Definition("{\"schemaVersion\":1,\"pages\":[]}"),
                    ClientFormsTestDocuments.Hash(new string('a', 64)),
                    0,
                    UserId,
                    Now
                ),
                null,
                null,
                2026,
                null,
                Now.AddDays(7),
                null,
                null,
                origin,
                origin == FormRequestOrigin.IntakeLink ? Guid.NewGuid() : null,
                UserId,
                UserId,
                Now
            )
            .Value;

    private sealed record FixtureState(
        FormParticipant Participant,
        FormRequest Request,
        FormAccessGrant Grant,
        MemoryGrantRepository Grants,
        MemoryChallengeRepository Challenges,
        MemorySessionRepository Sessions,
        ExternalRequestRepository Requests,
        MemoryParticipantRepository Participants,
        MemoryConsentNoticeRepository Notices,
        MemoryFormConsentRecordRepository ConsentRecords,
        FixedSecurity Security
    );

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("save failed");
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

    private sealed class FixedHostValidator(bool matches) : IFormPublicTenantHostValidator
    {
        public Task<bool> MatchesTenantAsync(Guid tenantId, string host, CancellationToken ct = default) =>
            Task.FromResult(matches);
    }

    private sealed class FixedCapabilities(bool external = true, bool intake = false)
        : IClientFormsPublicCapabilityEvaluator
    {
        public Task<bool> IsExternalParticipantAvailableAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(external);

        public Task<bool> IsIntakeLinkAvailableAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(intake);
    }

    private sealed class FixedSecurity : IFormExternalAccessSecurity
    {
        public FormOpaqueToken GenerateToken() => new("session-token", "session-hash", "oken");

        public FormProtectedToken GenerateProtectedToken() => throw new NotSupportedException();

        public string HashToken(string rawToken) =>
            rawToken switch
            {
                "access-token" => "access-hash",
                "session-token" => "session-hash",
                _ => "invalid",
            };

        public string UnprotectToken(string tokenCipher, short keyVersion) => throw new NotSupportedException();

        public string GenerateOtp() => "123456";

        public string HashOtp(Guid tenantId, Guid sourceId, string email, string code) => "otp-hash";

        public bool VerifyOtp(Guid tenantId, Guid sourceId, string email, string code, string storedHash) =>
            code == "123456" && storedHash == "otp-hash";

        public FormProtectedEmail ProtectEmail(Guid tenantId, string email) =>
            new($"cipher:{email}", 1, $"lookup:{email}");

        public string UnprotectEmail(string emailCipher, short keyVersion) => emailCipher["cipher:".Length..];

        public IReadOnlyCollection<string> EmailLookupHashes(Guid tenantId, string email) => [$"lookup:{email}"];
    }

    private sealed class CapturingPayloadCodec : IProtectedPayloadCodec
    {
        public object? Content { get; private set; }
        public ProtectedPayloadBinding? Binding { get; private set; }

        public ProtectedPayload Protect<T>(string schema, ProtectedPayloadBinding binding, T content)
        {
            Content = content;
            Binding = binding;
            return new ProtectedPayload
            {
                Cipher = "ciphertext",
                KeyVersion = 1,
                Schema = schema,
            };
        }

        public T Unprotect<T>(
            ProtectedPayload payload,
            string expectedSchema,
            ProtectedPayloadBinding expectedBinding
        ) => throw new NotSupportedException();
    }

    private sealed class MemoryParticipantRepository(params FormParticipant[] seed) : IFormParticipantRepository
    {
        private readonly List<FormParticipant> _items = [.. seed];

        public Task<FormParticipant?> GetAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
            Task.FromResult(_items.SingleOrDefault(item => item.TenantId == tenantId && item.Id == participantId));

        public Task<FormParticipant?> GetUnlinkedByEmailAsync(
            Guid tenantId,
            string email,
            CancellationToken ct = default
        ) => Task.FromResult(_items.SingleOrDefault(item => item.TenantId == tenantId && item.Email == email));

        public void Add(FormParticipant participant) => _items.Add(participant);

        public void Untrack(FormParticipant participant) { }
    }

    private sealed class MemoryGrantRepository(params FormAccessGrant[] seed) : IFormAccessGrantRepository
    {
        private readonly List<FormAccessGrant> _items = [.. seed];

        public Task<FormAccessGrant?> GetByRequestAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _items.SingleOrDefault(item => item.TenantId == tenantId && item.FormRequestId == requestId)
            );

        public Task<FormAccessGrant?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult(_items.SingleOrDefault(item => item.TokenHash == tokenHash));

        public void Add(FormAccessGrant grant) => _items.Add(grant);
    }

    private sealed class MemoryChallengeRepository : IFormAccessChallengeRepository
    {
        public List<FormAccessChallenge> Items { get; } = [];

        public Task<FormAccessChallenge?> GetAsync(Guid tenantId, Guid challengeId, CancellationToken ct = default) =>
            Task.FromResult(Items.SingleOrDefault(item => item.TenantId == tenantId && item.Id == challengeId));

        public Task<FormAccessChallenge?> GetLatestAsync(
            Guid tenantId,
            Guid? grantId,
            Guid? intakeLinkId,
            IReadOnlyCollection<string> emailLookupHashes,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Items
                    .Where(item =>
                        item.TenantId == tenantId
                        && item.GrantId == grantId
                        && item.IntakeLinkId == intakeLinkId
                        && emailLookupHashes.Contains(item.EmailLookupHash)
                    )
                    .OrderByDescending(item => item.IssuedAtUtc)
                    .FirstOrDefault()
            );

        public Task<int> CountIssuedAsync(
            Guid tenantId,
            IReadOnlyCollection<string> emailLookupHashes,
            DateTime sinceUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Items.Count(item =>
                    item.TenantId == tenantId
                    && emailLookupHashes.Contains(item.EmailLookupHash)
                    && item.IssuedAtUtc >= sinceUtc
                )
            );

        public Task<int> CountTenantIssuedAsync(Guid tenantId, DateTime sinceUtc, CancellationToken ct = default) =>
            Task.FromResult(Items.Count(item => item.TenantId == tenantId && item.IssuedAtUtc >= sinceUtc));

        public Task<bool> HasActiveBlockAsync(
            Guid tenantId,
            Guid? grantId,
            Guid? intakeLinkId,
            IReadOnlyCollection<string> emailLookupHashes,
            DateTime nowUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Items.Any(item =>
                    item.TenantId == tenantId
                    && item.GrantId == grantId
                    && item.IntakeLinkId == intakeLinkId
                    && emailLookupHashes.Contains(item.EmailLookupHash)
                    && item.IsBlockedAt(nowUtc)
                )
            );

        public void Add(FormAccessChallenge challenge) => Items.Add(challenge);
    }

    private sealed class MemorySessionRepository : IFormAccessSessionRepository
    {
        public List<FormAccessSession> Items { get; } = [];

        public Task<FormAccessSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult(Items.SingleOrDefault(item => item.SessionTokenHash == tokenHash));

        public Task<IReadOnlyList<FormAccessSession>> ListNotRevokedByRequestAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<FormAccessSession>>(
                Items
                    .Where(item =>
                        item.TenantId == tenantId && item.FormRequestId == requestId && item.RevokedAtUtc is null
                    )
                    .ToArray()
            );

        public void Add(FormAccessSession session) => Items.Add(session);
    }

    private sealed class ExternalRequestRepository(FormRequest request) : IFormRequestRepository
    {
        public void Add(FormRequest item) { }

        public Task<Result<FormRequest>> GetForCommandAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        ) => GetForReviewAsync(tenantId, requestId, ct);

        public Task<Result<FormRequest>> GetForReviewAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                tenantId == request.TenantId && requestId == request.Id
                    ? Result.Success(request)
                    : Result.Failure<FormRequest>(FormRequestErrors.NotFound)
            );

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
