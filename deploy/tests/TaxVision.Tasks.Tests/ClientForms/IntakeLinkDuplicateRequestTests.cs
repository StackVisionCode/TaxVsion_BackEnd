using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.ClientForms;

// Un enlace de captación sirve a muchos prospectos, pero la misma persona no abre dos requests desde él.
public sealed class IntakeLinkDuplicateRequestTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-0000000000d6");
    private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-0000000000d6");
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private const string Email = "prospect@example.com";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_open_request_from_the_same_link_is_resumed(bool returned)
    {
        var fixture = Fixture();
        var first = await FirstVisitAsync(fixture);
        if (returned)
        {
            SubmitFirst(fixture, first);
            Assert.True(fixture.Requests.Items.Single().ReturnForChanges("Fix it", [], UserId, Now).IsSuccess);
        }

        var second = await SecondVisitAsync(fixture, "intake-token");

        Assert.True(second.IsSuccess);
        Assert.Equal(first.RequestId, second.Value.RequestId);
        Assert.Single(fixture.Requests.Items);
        Assert.Equal(2, fixture.Sessions.Items.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_submitted_or_completed_request_blocks_a_new_one_with_an_explicit_outcome(bool completed)
    {
        var fixture = Fixture();
        var first = await FirstVisitAsync(fixture);
        SubmitFirst(fixture, first);
        if (completed)
            Assert.True(fixture.Requests.Items.Single().Complete(null, UserId, Now).IsSuccess);

        var second = await SecondVisitAsync(fixture, "intake-token");

        Assert.Equal(PublicIntakeFormErrors.AlreadySubmitted, second.Error);
        Assert.Equal("PublicForm.AlreadySubmitted", second.Error.Code);
        Assert.Equal("You already sent this form. Your office is reviewing it.", second.Error.Message);
        Assert.Single(fixture.Requests.Items);
        Assert.Single(fixture.Sessions.Items);
        // El código del segundo intento quedó consumido: no sirve para otro intento.
        Assert.All(fixture.Challenges.Items, challenge => Assert.NotNull(challenge.ConsumedAtUtc));
    }

    [Fact]
    public async Task A_cancelled_request_allows_a_new_one()
    {
        var fixture = Fixture();
        var first = await FirstVisitAsync(fixture);
        Assert.True(fixture.Requests.Items.Single().Cancel(null, UserId, Now).IsSuccess);

        var second = await SecondVisitAsync(fixture, "intake-token");

        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.RequestId, second.Value.RequestId);
        Assert.Equal(2, fixture.Requests.Items.Count);
    }

    [Fact]
    public async Task Another_intake_link_is_not_affected()
    {
        var fixture = Fixture();
        var first = await FirstVisitAsync(fixture);
        SubmitFirst(fixture, first);

        var other = await SecondVisitAsync(fixture, "intake-token-2");

        Assert.True(other.IsSuccess);
        Assert.NotEqual(first.RequestId, other.Value.RequestId);
        Assert.Equal(
            fixture.OtherLink.Id,
            fixture.Requests.Items.Single(item => item.Id == other.Value.RequestId).IntakeLinkId
        );
    }

    [Fact]
    public async Task The_open_request_of_a_participant_who_already_became_a_client_is_resumed()
    {
        var fixture = Fixture();
        var first = await FirstVisitAsync(fixture);
        var participant = fixture.Participants.Items.Single();
        Assert.True(participant.LinkToCustomer(Guid.NewGuid(), UserId, Now).IsSuccess);

        var second = await SecondVisitAsync(fixture, "intake-token");

        Assert.True(second.IsSuccess);
        Assert.Equal(first.RequestId, second.Value.RequestId);
        Assert.Single(fixture.Participants.Items);
        Assert.Equal(FormParticipantStatus.Linked, participant.Status);
    }

    private static async Task<PublicIntakeFormSessionResponse> FirstVisitAsync(State fixture)
    {
        await ChallengeAsync(fixture, "intake-token", Now);
        var verified = await VerifyAsync(fixture, "intake-token", Now.AddMinutes(1));
        Assert.True(verified.IsSuccess);
        return verified.Value;
    }

    private static async Task<Result<PublicIntakeFormSessionResponse>> SecondVisitAsync(State fixture, string token)
    {
        await ChallengeAsync(fixture, token, Now.AddMinutes(2));
        return await VerifyAsync(fixture, token, Now.AddMinutes(3));
    }

    private static void SubmitFirst(State fixture, PublicIntakeFormSessionResponse first)
    {
        var request = fixture.Requests.Items.Single(item => item.Id == first.RequestId);
        var submitted = request.Submit(
            new ProtectedAnswers("{\"schemaVersion\":1,\"values\":{}}", 1),
            [],
            [],
            FormActor.ExternalSession(request.ParticipantId!.Value, Guid.NewGuid()),
            Now
        );
        Assert.True(submitted.IsSuccess);
    }

    private static async Task ChallengeAsync(State fixture, string token, DateTime nowUtc)
    {
        var bus = new FakeMessageBus();
        var result = await RequestPublicIntakeFormChallengeHandler.Handle(
            new RequestPublicIntakeFormChallengeCommand(token, "office.test", Email, "captcha", "127.0.0.1", nowUtc),
            fixture.Links,
            fixture.Challenges,
            fixture.Versions,
            fixture.Notices,
            new ExtSecurity(),
            new ExtHost(),
            new ExtCapabilities(),
            new AllowBots(),
            new ExtCodec(),
            Options.Create(new ClientFormsOptions { External = new ClientFormsExternalOptions() }),
            new RecordingUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            new RecordingClientFormsMetrics(),
            default
        );
        Assert.True(result.IsSuccess);
        Assert.Single(bus.Published);
    }

    private static Task<Result<PublicIntakeFormSessionResponse>> VerifyAsync(
        State fixture,
        string token,
        DateTime nowUtc
    ) =>
        VerifyPublicIntakeFormChallengeHandler.Handle(
            new VerifyPublicIntakeFormChallengeCommand(
                token,
                "office.test",
                Email,
                "123456",
                "127.0.0.1",
                "tests",
                nowUtc
            ),
            fixture.Links,
            fixture.Challenges,
            fixture.Sessions,
            fixture.Participants,
            fixture.Requests,
            fixture.Versions,
            fixture.Notices,
            new MemoryFormConsentRecordRepository(),
            new ExtSecurity(),
            new ExtHost(),
            new ExtCapabilities(),
            new ExtAuditLog(),
            new RecordingUnitOfWork(),
            new RecordingClientFormsMetrics(),
            default
        );

    private static State Fixture()
    {
        var notice = FormTemplateHandlerTestDoubles.IdentityNotice(TenantId, UserId, Now.AddDays(-1));
        var version = Version();
        var otherVersion = Version();
        var link = Link(version, notice, "intake-hash");
        var other = Link(otherVersion, notice, "intake-hash-2");
        return new State(
            link,
            other,
            new Links(link, other),
            new ExtChallenges(),
            new ExtSessions(),
            new ExtParticipants(),
            new ExtRequests(),
            new MemoryFormTemplateVersionRepository(version, otherVersion),
            new MemoryConsentNoticeRepository(notice)
        );
    }

    private static FormTemplateVersion Version() =>
        FormTemplateVersion.Create(
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

    private static FormIntakeLink Link(
        FormTemplateVersion version,
        TaxVision.Tasks.ClientForms.Domain.Consents.ConsentNotice notice,
        string tokenHash
    ) =>
        FormIntakeLink
            .Create(
                TenantId,
                "Public intake",
                version.FormTemplateId,
                version.Id,
                notice.Id,
                notice.Versions.Single().VersionNumber,
                tokenHash,
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

    private sealed record State(
        FormIntakeLink Link,
        FormIntakeLink OtherLink,
        Links Links,
        ExtChallenges Challenges,
        ExtSessions Sessions,
        ExtParticipants Participants,
        ExtRequests Requests,
        MemoryFormTemplateVersionRepository Versions,
        MemoryConsentNoticeRepository Notices
    );

    private sealed class AllowBots : IFormIntakeBotProtection
    {
        public bool IsReady => true;

        public Task<bool> VerifyAsync(string token, string clientIp, CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    private sealed class Links(params FormIntakeLink[] seed) : IFormIntakeLinkRepository
    {
        private readonly List<FormIntakeLink> _items = [.. seed];

        public Task<FormIntakeLink?> GetAsync(Guid tenantId, Guid linkId, CancellationToken ct = default) =>
            Task.FromResult(_items.SingleOrDefault(item => item.TenantId == tenantId && item.Id == linkId));

        public Task<FormIntakeLink?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult(_items.SingleOrDefault(item => item.TokenHash == tokenHash));

        public Task<IReadOnlyList<FormIntakeLink>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FormIntakeLink>>(_items.Where(item => item.TenantId == tenantId).ToArray());

        public void Add(FormIntakeLink link) => _items.Add(link);
    }
}
