using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Api.Controllers.Public.ClientForms;
using TaxVision.Tasks.Api.Requests.ClientForms;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Tests.Dependencies;
using static TaxVision.Tasks.Tests.ClientForms.FormActionTestDoubles;

namespace TaxVision.Tasks.Tests.ClientForms;

// El 202 del challenge informa el cupo de códigos por hora y es idéntico se haya enviado o no el código.
public sealed class PublicFormChallengeResponseTests
{
    private const string ExpectedBody = "{\"codeLimitPerHour\":5,\"retryAfterSeconds\":null}";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task The_response_is_byte_identical_when_the_code_is_sent_and_when_it_is_throttled()
    {
        var state = DirectedState(seededIssues: 0);
        var sent = await ChallengeAsync(state);
        Assert.Single(state.Published);

        var throttledState = DirectedState(seededIssues: PublicFormChallengeAcceptedResponse.CodeLimit);
        var throttled = await ChallengeAsync(throttledState);
        Assert.Empty(throttledState.Published);

        Assert.Equal(StatusCodes.Status202Accepted, sent.Status);
        Assert.Equal(sent.Status, throttled.Status);
        Assert.Equal(ExpectedBody, sent.Body);
        Assert.Equal(sent.Body, throttled.Body);
    }

    [Fact]
    public async Task An_unknown_token_or_a_missing_host_answer_with_the_same_body()
    {
        var state = DirectedState(seededIssues: 0);

        var unknown = await ChallengeAsync(state, token: "other-token");
        var noHost = await ChallengeAsync(state, origin: null);

        Assert.Equal(ExpectedBody, unknown.Body);
        Assert.Equal(ExpectedBody, noHost.Body);
        Assert.Empty(state.Published);
    }

    [Fact]
    public async Task The_intake_challenge_exposes_the_same_constant_hint()
    {
        var controller = Controller(
            InvokingMessageBus.Create(_ => Task.FromResult(Result.Success())),
            "https://office.test"
        );

        var result = await controller.ChallengeIntake(
            "intake-token",
            new RequestPublicIntakeChallengeRequest("prospect@example.com", "captcha-token"),
            default
        );

        Assert.Equal(ExpectedBody, Serialize(Assert.IsType<AcceptedResult>(result).Value));
    }

    private static async Task<(int? Status, string Body)> ChallengeAsync(
        State state,
        string token = "access-token",
        string? origin = "https://office.test"
    )
    {
        var bus = InvokingMessageBus.Create(message =>
            RequestPublicDirectedFormChallengeHandler.Handle(
                (RequestPublicDirectedFormChallengeCommand)message,
                state.Grants,
                state.Challenges,
                state.Requests,
                state.Participants,
                state.Notices,
                new ExtSecurity(),
                new ExtHost(),
                new ExtCapabilities(),
                Options.Create(new ClientFormsOptions { External = new ClientFormsExternalOptions() }),
                new ExtCodec(),
                new RecordingUnitOfWork(),
                state.Bus,
                new NoOpCorrelationContext(),
                new RecordingClientFormsMetrics(),
                default
            )
        );
        var result = Assert.IsType<AcceptedResult>(await Controller(bus, origin).Challenge(token, default));
        return (result.StatusCode, Serialize(result.Value));
    }

    private static PublicFormsController Controller(Wolverine.IMessageBus bus, string? origin)
    {
        var http = new DefaultHttpContext();
        if (origin is not null)
            http.Request.Headers.Origin = origin;
        return new PublicFormsController(bus) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static string Serialize(object? value) => JsonSerializer.Serialize(value, Json);

    // Controller usa la hora real: el enlace y los códigos sembrados se fechan respecto de ella.
    private static State DirectedState(int seededIssues)
    {
        var nowUtc = DateTime.UtcNow;
        var participant = FormParticipant
            .Create(TenantId, "guest@example.com", "Guest", null, FormParticipantSource.DirectedRequest, nowUtc)
            .Value;
        var request = ExternalRequest(Version(), participant);
        var notice = FormTemplateHandlerTestDoubles.IdentityNotice(TenantId, Responsible, nowUtc.AddDays(-1));
        var version = notice.Versions.Single();
        var grant = FormAccessGrant
            .Create(
                TenantId,
                request.Id,
                notice.Id,
                version.VersionNumber,
                "access-hash",
                "oken",
                nowUtc.AddDays(14),
                nowUtc
            )
            .Value;
        var challenges = new ExtChallenges();
        var security = new ExtSecurity();
        var email = security.ProtectEmail(TenantId, participant.Email);
        for (var index = 0; index < seededIssues; index++)
        {
            var issuedAt = nowUtc.AddMinutes(-50 + index);
            challenges.Add(
                FormAccessChallenge
                    .Create(
                        TenantId,
                        grant.Id,
                        null,
                        email.Cipher,
                        email.KeyVersion,
                        email.LookupHash,
                        "otp-hash",
                        version,
                        issuedAt.AddMinutes(10),
                        issuedAt
                    )
                    .Value
            );
        }
        return new State(
            new ExtGrants(grant),
            challenges,
            new ExtRequests(request),
            new ExtParticipants(participant),
            new MemoryConsentNoticeRepository(notice),
            new FakeMessageBus()
        );
    }

    private sealed record State(
        ExtGrants Grants,
        ExtChallenges Challenges,
        ExtRequests Requests,
        ExtParticipants Participants,
        MemoryConsentNoticeRepository Notices,
        FakeMessageBus Bus
    )
    {
        public IReadOnlyList<object> Published => Bus.Published;
    }
}
