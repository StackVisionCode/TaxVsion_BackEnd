using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ConsentDomainTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 22, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Published_versions_are_immutable_and_numbered_by_the_aggregate()
    {
        var notice = Notice();

        var first = Publish(notice, "First notice");
        var second = Publish(notice, "Second notice");

        Assert.Equal(1, first.VersionNumber);
        Assert.Equal(2, second.VersionNumber);
        Assert.Equal(2, notice.LatestVersionNumber);
        Assert.Equal(2, notice.Versions.Count);
        Assert.Equal("First notice", first.TextSnapshot);
    }

    [Fact]
    public void Accepted_evidence_uses_the_server_notice_snapshot_and_actor()
    {
        var version = Publish(Notice(), "Privacy notice");
        var participantId = Guid.NewGuid();
        var actor = FormActor.ExternalSession(participantId, Guid.NewGuid());

        var result = FormConsentRecord.Create(
            TenantId,
            Guid.NewGuid(),
            "fd_consent01",
            FormParticipantKind.External,
            null,
            participantId,
            actor,
            version,
            "I agree",
            FormConsentOutcome.Accepted,
            "203.0.113.10",
            "tests",
            Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(version.ContentHash, result.Value.NoticeContentHash);
        Assert.Equal(version.TextSnapshot, result.Value.TextSnapshot);
        Assert.Equal(participantId, result.Value.AcceptedBy.ParticipantId);
    }

    [Fact]
    public void Participant_shape_must_match_the_declared_kind()
    {
        var version = Publish(Notice(), "Privacy notice");

        var result = FormConsentRecord.Create(
            TenantId,
            Guid.NewGuid(),
            "fd_consent01",
            FormParticipantKind.Customer,
            null,
            Guid.NewGuid(),
            FormActor.PortalUser(UserId),
            version,
            "I agree",
            FormConsentOutcome.Accepted,
            "203.0.113.10",
            "tests",
            Now
        );

        Assert.Equal(ConsentErrors.RecordInvalid, result.Error);
    }

    [Fact]
    public void Evidence_can_only_be_bound_to_one_revision()
    {
        var version = Publish(Notice(), "Privacy notice");
        var customerId = Guid.NewGuid();
        var record = FormConsentRecord
            .Create(
                TenantId,
                Guid.NewGuid(),
                "fd_consent01",
                FormParticipantKind.Customer,
                customerId,
                null,
                FormActor.PortalUser(UserId),
                version,
                "I agree",
                FormConsentOutcome.Accepted,
                "203.0.113.10",
                "tests",
                Now
            )
            .Value;

        Assert.True(record.BindToRevision(1).IsSuccess);
        Assert.Equal(ConsentErrors.RecordInvalid, record.BindToRevision(2).Error);
    }

    [Fact]
    public void Notice_rejects_an_unknown_purpose()
    {
        var result = ConsentNotice.Create(
            TenantId,
            "privacy_notice",
            "Privacy notice",
            (ConsentNoticePurpose)99,
            UserId,
            Now
        );

        Assert.Equal(ConsentErrors.NoticeInvalid, result.Error);
    }

    [Fact]
    public void Evidence_keeps_the_canonical_body_covered_by_the_hash()
    {
        var version = Publish(Notice(), "Privacy notice");
        var customerId = Guid.NewGuid();

        var record = FormConsentRecord
            .Create(
                TenantId,
                Guid.NewGuid(),
                "fd_consent01",
                FormParticipantKind.Customer,
                customerId,
                null,
                FormActor.PortalUser(UserId),
                version,
                null,
                FormConsentOutcome.Declined,
                "203.0.113.10",
                "tests",
                Now
            )
            .Value;

        Assert.Equal(version.BodyJson, record.NoticeBodyJson);
        Assert.Equal(version.ContentHash, record.NoticeContentHash);
    }

    private static ConsentNotice Notice() =>
        ConsentNotice
            .Create(TenantId, "privacy_notice", "Privacy notice", ConsentNoticePurpose.General, UserId, Now)
            .Value;

    private static ConsentNoticeVersion Publish(ConsentNotice notice, string text) =>
        notice.Publish("en", $"{{\"type\":\"doc\",\"text\":\"{text}\"}}", text, new string('a', 64), UserId, Now).Value;
}
