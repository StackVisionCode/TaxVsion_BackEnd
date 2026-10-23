using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormExternalParticipantDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Participant_normalizes_email_and_link_is_immutable()
    {
        var participant = FormParticipant
            .Create(
                Guid.NewGuid(),
                "  USER@Example.COM ",
                " User ",
                "+18095551234",
                FormParticipantSource.DirectedRequest,
                Now
            )
            .Value;
        var customerId = Guid.NewGuid();

        Assert.Equal("user@example.com", participant.Email);
        Assert.Equal("User", participant.FullName);
        Assert.True(participant.MarkVerified(Now.AddMinutes(1)).IsSuccess);
        Assert.True(participant.LinkToCustomer(customerId, Guid.NewGuid(), Now.AddMinutes(2)).IsSuccess);
        Assert.True(participant.LinkToCustomer(customerId, Guid.NewGuid(), Now.AddMinutes(3)).IsSuccess);
        Assert.Equal(
            FormParticipantErrors.AlreadyLinked,
            participant.LinkToCustomer(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(4)).Error
        );
        Assert.Equal(FormParticipantErrors.InvalidTransition, participant.UpdateContact("Other", null).Error);
    }

    [Fact]
    public void Intake_link_counts_only_submissions_and_enforces_limit()
    {
        var link = Intake(maxSubmissions: 2);

        Assert.True(link.RegisterSubmission(Now.AddMinutes(1)).IsSuccess);
        Assert.True(link.RegisterSubmission(Now.AddMinutes(2)).IsSuccess);
        Assert.Equal(FormParticipantErrors.IntakeLimitReached, link.RegisterSubmission(Now.AddMinutes(3)).Error);
        Assert.Equal(2, link.SubmissionCount);
    }

    [Fact]
    public void Revoked_intake_link_cannot_resume_or_regenerate_token()
    {
        var link = Intake(maxSubmissions: null);

        link.Revoke();

        Assert.Equal(FormParticipantErrors.IntakeLinkUnavailable, link.Resume(Now.AddMinutes(1)).Error);
        Assert.Equal(
            FormParticipantErrors.IntakeLinkInvalid,
            link.RegenerateToken("hash2", "1234", "cipher2", 1).Error
        );
    }

    [Fact]
    public void Access_grant_rotates_up_to_five_issues_and_rejects_old_lifecycle()
    {
        var tenantId = Guid.NewGuid();
        var notice = NoticeVersion(tenantId);
        var grant = FormAccessGrant
            .Create(
                tenantId,
                Guid.NewGuid(),
                notice.ConsentNoticeId,
                notice.VersionNumber,
                "hash1",
                "0001",
                Now.AddDays(14),
                Now
            )
            .Value;

        for (var issue = 2; issue <= FormAccessGrant.MaxIssues; issue++)
            Assert.True(
                grant.Rotate($"hash{issue}", issue.ToString("0000"), Now.AddDays(14), Now.AddMinutes(issue)).IsSuccess
            );

        Assert.Equal(
            FormParticipantErrors.AccessGrantIssueLimit,
            grant.Rotate("hash6", "0006", Now.AddDays(14), Now.AddMinutes(6)).Error
        );
        Assert.Equal(FormAccessGrant.MaxIssues, grant.IssueCount);
    }

    [Fact]
    public void Challenge_blocks_after_five_failed_attempts_and_is_single_use()
    {
        var tenantId = Guid.NewGuid();
        var notice = NoticeVersion(tenantId);
        var challenge = FormAccessChallenge
            .Create(
                tenantId,
                Guid.NewGuid(),
                null,
                "email-cipher",
                1,
                "v1:lookup",
                "hash",
                notice,
                Now.AddMinutes(10),
                Now
            )
            .Value;

        for (var attempt = 1; attempt <= FormAccessChallenge.MaxAttempts; attempt++)
            Assert.Equal(
                FormParticipantErrors.ChallengeCodeMismatch,
                challenge.Verify(false, Now.AddSeconds(attempt)).Error
            );

        Assert.Equal(FormParticipantErrors.ChallengeBlocked, challenge.Verify(true, Now.AddMinutes(1)).Error);
        Assert.Equal(
            FormParticipantErrors.ChallengeExpired,
            challenge.Verify(true, Now.Add(FormAccessChallenge.BlockDuration).AddSeconds(6)).Error
        );

        var valid = FormAccessChallenge
            .Create(
                tenantId,
                Guid.NewGuid(),
                null,
                "email-cipher",
                1,
                "v1:lookup",
                "hash",
                notice,
                Now.AddMinutes(10),
                Now
            )
            .Value;
        Assert.True(valid.Verify(true, Now.AddMinutes(1)).IsSuccess);
        Assert.Equal(FormParticipantErrors.ChallengeConsumed, valid.Verify(true, Now.AddMinutes(2)).Error);
    }

    [Fact]
    public void Blocked_challenge_does_not_allow_a_reissue_until_the_block_ends()
    {
        var tenantId = Guid.NewGuid();
        var challenge = FormAccessChallenge
            .Create(
                tenantId,
                Guid.NewGuid(),
                null,
                "email-cipher",
                1,
                "v1:lookup",
                "hash",
                NoticeVersion(tenantId),
                Now.AddMinutes(10),
                Now
            )
            .Value;

        Assert.False(challenge.AllowsReissueAt(Now.AddSeconds(29)));
        Assert.True(challenge.AllowsReissueAt(Now.Add(FormAccessChallenge.ResendCooldown)));
        for (var attempt = 1; attempt <= FormAccessChallenge.MaxAttempts; attempt++)
            challenge.Verify(false, Now.AddSeconds(attempt));
        var blockedUntil = challenge.BlockedUntilUtc!.Value;

        Assert.True(challenge.IsBlockedAt(Now.AddMinutes(5)));
        Assert.False(challenge.AllowsReissueAt(Now.AddMinutes(5)));
        Assert.False(challenge.IsBlockedAt(blockedUntil));
        Assert.True(challenge.AllowsReissueAt(blockedUntil));
    }

    [Fact]
    public void Session_uses_sliding_expiry_without_exceeding_absolute_timeout()
    {
        var session = FormAccessSession
            .Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hash", "127.0.0.1", "test-agent", Now)
            .Value;

        Assert.True(session.Touch(Now.AddMinutes(29)).IsSuccess);
        Assert.Equal(Now.AddMinutes(59), session.ExpiresAtUtc);
        Assert.True(session.Touch(Now.AddHours(3).AddMinutes(50)).IsFailure);

        var active = FormAccessSession
            .Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hash", "127.0.0.1", "test-agent", Now)
            .Value;
        Assert.True(active.Touch(Now.AddMinutes(20)).IsSuccess);
        Assert.True(active.Touch(Now.AddMinutes(40)).IsSuccess);
        Assert.True(active.Touch(Now.AddHours(3).AddMinutes(40)).IsFailure);
    }

    private static FormIntakeLink Intake(int? maxSubmissions) =>
        FormIntakeLink
            .Create(
                Guid.NewGuid(),
                "Public organizer",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                "hash",
                "1234",
                "cipher",
                1,
                Now.AddDays(30),
                maxSubmissions,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now
            )
            .Value;

    private static ConsentNoticeVersion NoticeVersion(Guid tenantId) =>
        FormTemplateHandlerTestDoubles.IdentityNotice(tenantId, Guid.NewGuid(), Now).Versions.Single();
}
