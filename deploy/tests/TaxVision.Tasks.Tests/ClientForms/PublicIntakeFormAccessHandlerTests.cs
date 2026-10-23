using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class PublicIntakeFormAccessHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Challenge_is_a_noop_when_bot_protection_rejects_the_request()
    {
        var fixture = Fixture();
        var bus = new FakeMessageBus();

        var result = await ChallengeAsync(fixture, new FixedBotProtection(false), bus);

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Published);
        Assert.Empty(fixture.Challenges.Items);
    }

    [Fact]
    public async Task Challenge_uses_intake_binding_without_inventing_request_or_participant()
    {
        var fixture = Fixture();
        var codec = new CapturingPayloadCodec();
        var bus = new FakeMessageBus();

        var result = await ChallengeAsync(fixture, new FixedBotProtection(true), bus, codec);

        Assert.True(result.IsSuccess);
        var message = Assert.IsType<FormAccessCodeDeliveryRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Null(message.FormRequestId);
        Assert.Null(message.Participant);
        Assert.Equal(fixture.Link.Id, message.IntakeLinkId);
        Assert.Equal(fixture.Link.Id, codec.Binding!.IntakeLinkId);
        Assert.Null(codec.Binding.FormRequestId);
        Assert.Null(codec.Binding.Participant);
        Assert.Equal(message.ChallengeId, codec.Binding.ChallengeId);
        Assert.Equal("test", message.CorrelationId);
    }

    [Fact]
    public async Task Valid_code_creates_verified_participant_request_and_bound_session()
    {
        var fixture = Fixture();
        await ChallengeAsync(fixture, new FixedBotProtection(true), new FakeMessageBus());

        var result = await VerifyPublicIntakeFormChallengeHandler.Handle(
            new VerifyPublicIntakeFormChallengeCommand(
                "intake-token",
                "office.test",
                "prospect@example.com",
                "123456",
                "127.0.0.1",
                "tests",
                Now.AddMinutes(1)
            ),
            fixture.Links,
            fixture.Challenges,
            fixture.Sessions,
            fixture.Participants,
            fixture.Requests,
            fixture.Versions,
            fixture.Notices,
            fixture.ConsentRecords,
            fixture.Security,
            new FixedHostValidator(),
            new FixedCapabilities(),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            default
        );

        Assert.True(result.IsSuccess);
        var participant = Assert.Single(fixture.Participants.Items);
        Assert.Equal(FormParticipantStatus.Verified, participant.Status);
        Assert.Equal(FormParticipantSource.IntakeLink, participant.Source);
        var request = Assert.Single(fixture.Requests.Items);
        Assert.Equal(FormRequestOrigin.IntakeLink, request.Origin);
        Assert.Equal(fixture.Link.Id, request.IntakeLinkId);
        Assert.Equal(participant.Id, request.ParticipantId);
        var session = Assert.Single(fixture.Sessions.Items);
        Assert.Equal(request.Id, session.FormRequestId);
        Assert.Equal(request.Id, result.Value.RequestId);
        var evidence = Assert.Single(fixture.ConsentRecords.Items);
        Assert.Equal(FormConsentOutcome.Presented, evidence.Outcome);
        Assert.Equal("entry_notice", evidence.ComponentId);
        Assert.Equal(participant.Id, evidence.ParticipantId);
    }

    [Fact]
    public async Task Concurrent_first_verify_for_the_same_email_reuses_the_participant_that_won_the_index()
    {
        var fixture = Fixture();
        await ChallengeAsync(fixture, new FixedBotProtection(true), new FakeMessageBus());
        var winner = FormParticipant
            .Create(TenantId, "prospect@example.com", null, null, FormParticipantSource.IntakeLink, Now)
            .Value;
        var participants = new RacingParticipantRepository(winner);

        var result = await VerifyPublicIntakeFormChallengeHandler.Handle(
            VerifyCommand("123456", Now.AddMinutes(1)),
            fixture.Links,
            fixture.Challenges,
            fixture.Sessions,
            participants,
            fixture.Requests,
            fixture.Versions,
            fixture.Notices,
            fixture.ConsentRecords,
            fixture.Security,
            new FixedHostValidator(),
            new FixedCapabilities(),
            new RecordingFormAuditLog(),
            new ConflictingUnitOfWork(1, "Persistence.UniqueConstraint"),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.True(participants.LoserUntracked);
        Assert.Equal(winner.Id, Assert.Single(fixture.Sessions.Items).ParticipantId);
        Assert.Equal(FormParticipantStatus.Verified, winner.Status);
    }

    [Fact]
    public async Task Pause_and_revoke_work_while_intake_is_unavailable_but_resume_does_not()
    {
        var fixture = Fixture();
        var capabilities = new ClosedCapabilities();

        var paused = await UpdateLinkAsync(fixture, FormIntakeLinkAction.Pause, capabilities);
        var resumed = await UpdateLinkAsync(fixture, FormIntakeLinkAction.Resume, capabilities);
        var revoked = await UpdateLinkAsync(fixture, FormIntakeLinkAction.Revoke, capabilities);

        Assert.True(paused.IsSuccess);
        Assert.Equal(FormParticipantErrors.IntakeLinkUnavailable, resumed.Error);
        Assert.True(revoked.IsSuccess);
        Assert.Equal(FormIntakeLinkStatus.Revoked, fixture.Link.Status);
    }

    private static Task<Result<FormIntakeLinkTokenResponse>> UpdateLinkAsync(
        FixtureState fixture,
        FormIntakeLinkAction action,
        IClientFormsPublicCapabilityEvaluator capabilities
    ) =>
        UpdateFormIntakeLinkHandler.Handle(
            new UpdateFormIntakeLinkCommand(TenantId, UserId, fixture.Link.Id, action, null, [], Now.AddMinutes(1)),
            capabilities,
            fixture.Links,
            fixture.Versions,
            fixture.Security,
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            default
        );

    private static VerifyPublicIntakeFormChallengeCommand VerifyCommand(string code, DateTime nowUtc) =>
        new("intake-token", "office.test", "prospect@example.com", code, "127.0.0.1", "tests", nowUtc);

    private static Task<Result> ChallengeAsync(
        FixtureState fixture,
        IFormIntakeBotProtection bot,
        FakeMessageBus bus,
        CapturingPayloadCodec? codec = null
    ) =>
        RequestPublicIntakeFormChallengeHandler.Handle(
            new RequestPublicIntakeFormChallengeCommand(
                "intake-token",
                "office.test",
                "prospect@example.com",
                "captcha-token",
                "127.0.0.1",
                Now
            ),
            fixture.Links,
            fixture.Challenges,
            fixture.Versions,
            fixture.Notices,
            fixture.Security,
            new FixedHostValidator(),
            new FixedCapabilities(),
            bot,
            codec ?? new CapturingPayloadCodec(),
            Options.Create(new ClientFormsOptions { Enabled = true, External = new ClientFormsExternalOptions() }),
            new RecordingUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            default
        );

    private static FixtureState Fixture()
    {
        var version = FormTemplateVersion.Create(
            TenantId,
            Guid.NewGuid(),
            1,
            "Public intake",
            FormPurpose.ClientIntake,
            FormPresentation.Wizard,
            ClientFormsTestDocuments.Definition("{\"schemaVersion\":1,\"pages\":[]}"),
            ClientFormsTestDocuments.Hash(new string('a', 64)),
            0,
            UserId,
            Now
        );
        var notice = FormTemplateHandlerTestDoubles.IdentityNotice(TenantId, UserId, Now);
        var noticeVersion = notice.Versions.Single();
        var link = FormIntakeLink
            .Create(
                TenantId,
                "Public intake",
                version.FormTemplateId,
                version.Id,
                notice.Id,
                noticeVersion.VersionNumber,
                "intake-hash",
                "oken",
                "cipher",
                1,
                Now.AddDays(30),
                20,
                UserId,
                UserId,
                Now
            )
            .Value;
        return new FixtureState(
            link,
            new MemoryIntakeLinkRepository(link),
            new MemoryChallengeRepository(),
            new MemorySessionRepository(),
            new MemoryParticipantRepository(),
            new MemoryRequestRepository(),
            new MemoryFormTemplateVersionRepository(version),
            new MemoryConsentNoticeRepository(notice),
            new MemoryFormConsentRecordRepository(),
            new FixedSecurity()
        );
    }

    private sealed record FixtureState(
        FormIntakeLink Link,
        MemoryIntakeLinkRepository Links,
        MemoryChallengeRepository Challenges,
        MemorySessionRepository Sessions,
        MemoryParticipantRepository Participants,
        MemoryRequestRepository Requests,
        MemoryFormTemplateVersionRepository Versions,
        MemoryConsentNoticeRepository Notices,
        MemoryFormConsentRecordRepository ConsentRecords,
        FixedSecurity Security
    );

    private sealed class FixedBotProtection(bool allowed) : IFormIntakeBotProtection
    {
        public bool IsReady => true;

        public Task<bool> VerifyAsync(string token, string clientIp, CancellationToken ct = default) =>
            Task.FromResult(allowed);
    }

    private sealed class FixedCapabilities : IClientFormsPublicCapabilityEvaluator
    {
        public Task<bool> IsExternalParticipantAvailableAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> IsIntakeLinkAvailableAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    private sealed class ClosedCapabilities : IClientFormsPublicCapabilityEvaluator
    {
        public Task<bool> IsExternalParticipantAvailableAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> IsIntakeLinkAvailableAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    // Antes del choque no hay participante; tras soltar el alta perdedora aparece la fila ganadora.
    private sealed class RacingParticipantRepository(FormParticipant winner) : IFormParticipantRepository
    {
        public bool LoserUntracked { get; private set; }

        public Task<FormParticipant?> GetAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
            Task.FromResult<FormParticipant?>(LoserUntracked && participantId == winner.Id ? winner : null);

        public Task<FormParticipant?> GetUnlinkedByEmailAsync(
            Guid tenantId,
            string email,
            CancellationToken ct = default
        ) => Task.FromResult<FormParticipant?>(LoserUntracked ? winner : null);

        public void Add(FormParticipant participant) { }

        public void Untrack(FormParticipant participant) => LoserUntracked = participant.Id != winner.Id;
    }

    private sealed class FixedHostValidator : IFormPublicTenantHostValidator
    {
        public Task<bool> MatchesTenantAsync(Guid tenantId, string host, CancellationToken ct = default) =>
            Task.FromResult(host == "office.test");
    }

    private sealed class FixedSecurity : IFormExternalAccessSecurity
    {
        public FormOpaqueToken GenerateToken() => new("session-token", "session-hash", "oken");

        public FormProtectedToken GenerateProtectedToken() => throw new NotSupportedException();

        public string HashToken(string rawToken) => rawToken == "intake-token" ? "intake-hash" : "invalid";

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
        public ProtectedPayloadBinding? Binding { get; private set; }

        public ProtectedPayload Protect<T>(string schema, ProtectedPayloadBinding binding, T content)
        {
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

    private sealed class MemoryIntakeLinkRepository(params FormIntakeLink[] seed) : IFormIntakeLinkRepository
    {
        private readonly List<FormIntakeLink> items = [.. seed];

        public Task<FormIntakeLink?> GetAsync(Guid tenantId, Guid linkId, CancellationToken ct = default) =>
            Task.FromResult(items.SingleOrDefault(item => item.TenantId == tenantId && item.Id == linkId));

        public Task<FormIntakeLink?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult(items.SingleOrDefault(item => item.TokenHash == tokenHash));

        public Task<IReadOnlyList<FormIntakeLink>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FormIntakeLink>>(items.Where(item => item.TenantId == tenantId).ToArray());

        public void Add(FormIntakeLink link) => items.Add(link);
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

    private sealed class MemoryParticipantRepository : IFormParticipantRepository
    {
        public List<FormParticipant> Items { get; } = [];

        public Task<FormParticipant?> GetAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
            Task.FromResult(Items.SingleOrDefault(item => item.TenantId == tenantId && item.Id == participantId));

        public Task<FormParticipant?> GetUnlinkedByEmailAsync(
            Guid tenantId,
            string email,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Items.SingleOrDefault(item =>
                    item.TenantId == tenantId && item.Email == email && item.LinkedCustomerId is null
                )
            );

        public void Add(FormParticipant participant) => Items.Add(participant);

        public void Untrack(FormParticipant participant) { }
    }

    private sealed class MemoryRequestRepository : IFormRequestRepository
    {
        public List<FormRequest> Items { get; } = [];

        public void Add(FormRequest request) => Items.Add(request);

        public Task<Result<FormRequest>> GetForCommandAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        ) => GetForReviewAsync(tenantId, requestId, ct);

        public Task<Result<FormRequest>> GetForReviewAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        )
        {
            var item = Items.SingleOrDefault(row => row.TenantId == tenantId && row.Id == requestId);
            return Task.FromResult(
                item is null ? Result.Failure<FormRequest>(FormRequestErrors.NotFound) : Result.Success(item)
            );
        }

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

        public Task<FormRequest?> GetOpenExternalRequestAsync(
            Guid tenantId,
            Guid participantId,
            Guid templateId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Items.SingleOrDefault(item =>
                    item.TenantId == tenantId
                    && item.ParticipantId == participantId
                    && item.FormTemplateId == templateId
                )
            );

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
