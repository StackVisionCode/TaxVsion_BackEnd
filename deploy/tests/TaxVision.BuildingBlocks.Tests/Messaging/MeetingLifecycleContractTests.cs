using System.Text.Json;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// Los eventos de reunión que publica Communication (Node, camelCase) deserializan en los records
/// .NET (F6): los campos nuevos de la invitación, y los cuatro eventos ampliados o nuevos. Un fixture
/// por evento, con la forma exacta que escribe Node.
/// </summary>
public sealed class MeetingLifecycleContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid MeetingId = Guid.Parse("5c0a2d6e-9d7b-4c1f-8e2a-3b4c5d6e7f80");
    private static readonly Guid HostId = Guid.Parse("2b91f0c4-1111-4222-8333-444455556666");
    private static readonly Guid InviteeId = Guid.Parse("1a2b3c4d-3333-4c4d-9e5f-000000000003");

    [Fact]
    public void Invitation_created_maps_the_meeting_details_added_in_F6()
    {
        var evt = Deserialize<MeetingInvitationCreatedIntegrationEvent>("communication.meeting.invitation_created.v1");

        Assert.Equal(MeetingId, evt.MeetingId);
        Assert.Equal("External", evt.InviteeKind);
        Assert.Null(evt.InviteeUserId);
        Assert.Equal("Revisión de la 1040", evt.MeetingTitle);
        Assert.Equal("Scheduled", evt.MeetingStatus);
        Assert.Equal("ABC-123", evt.ShortCode);
        Assert.Equal(new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc), evt.ScheduledForUtc);
        Assert.Equal(45, evt.DurationMinutes);
        Assert.Equal(HostId, evt.HostUserId);
        Assert.Equal("María Pérez", evt.HostDisplayName);
        Assert.Equal("America/Santo_Domingo", evt.TimeZone);
        Assert.Equal(HostId, evt.InvitedByUserId);
    }

    /// <summary>Un publisher anterior a F6 (sin los campos nuevos) sigue deserializando.</summary>
    [Fact]
    public void Invitation_created_without_the_new_fields_still_deserializes()
    {
        const string legacy = """
            {"eventId":"6f1c2a3b-1111-4a2b-9c3d-000000000011","tenantId":"4c12d117-766d-479c-a7ce-dfb779a1829b",
             "invitationId":"9b0d2c4e-2222-4b3c-8d4e-000000000012","meetingId":"5c0a2d6e-9d7b-4c1f-8e2a-3b4c5d6e7f80",
             "inviteeKind":"Customer","inviteeUserId":"1a2b3c4d-3333-4c4d-9e5f-000000000003","inviteeEmail":"c@example.com",
             "tokenHash":"h","expiresAtUtc":"2026-11-05T19:00:00.000Z","joinUrl":"https://x/y"}
            """;

        var evt = JsonSerializer.Deserialize<MeetingInvitationCreatedIntegrationEvent>(legacy, Json);

        Assert.NotNull(evt);
        Assert.Null(evt.MeetingTitle);
        Assert.Null(evt.ScheduledForUtc);
        Assert.Null(evt.TimeZone);
    }

    [Fact]
    public void Cancelled_maps_participants_invitees_and_the_meeting_details()
    {
        var evt = Deserialize<MeetingCancelledIntegrationEvent>("communication.meeting.cancelled.v1");

        Assert.Equal(MeetingId, evt.MeetingId);
        Assert.Equal(HostId, evt.CancelledByUserId);
        Assert.Equal(2, evt.ParticipantUserIds.Count);
        Assert.Equal([InviteeId], evt.InviteeUserIds);
        Assert.Equal(["externo@example.com"], evt.InvitedEmails);
        Assert.Equal("Client asked to move it", evt.Reason);
        Assert.Equal(new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc), evt.ScheduledForUtc);
        Assert.Equal("America/Santo_Domingo", evt.TimeZone);
    }

    [Fact]
    public void Rescheduled_maps_both_times()
    {
        var evt = Deserialize<MeetingRescheduledIntegrationEvent>("communication.meeting.rescheduled.v1");

        Assert.Equal(new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc), evt.PreviousScheduledForUtc);
        Assert.Equal(new DateTime(2026, 11, 5, 20, 0, 0, DateTimeKind.Utc), evt.NewScheduledForUtc);
        Assert.Equal(new DateTime(2026, 10, 30, 15, 4, 5, DateTimeKind.Utc), evt.RescheduledAtUtc);
        Assert.Equal([InviteeId], evt.InviteeUserIds);
        Assert.Equal(45, evt.DurationMinutes);
    }

    [Fact]
    public void Reminder_maps_minutes_before_and_the_join_urls()
    {
        var evt = Deserialize<MeetingReminderIntegrationEvent>("communication.meeting.reminder.v1");

        Assert.Equal(15, evt.MinutesBefore);
        Assert.Equal(new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc), evt.ScheduledForUtc);
        Assert.Equal("https://manfer.taxoffix.io/meetings", evt.StaffJoinUrl);
        Assert.Equal("https://manfer.taxoffix.io/portal/client/meetings", evt.PortalJoinUrl);
        Assert.Equal([HostId], evt.ParticipantUserIds);
        Assert.Equal(["externo@example.com"], evt.InvitedEmails);
    }

    [Fact]
    public void Invitation_revoked_maps_the_invitee_and_ejected()
    {
        var evt = Deserialize<MeetingInvitationRevokedIntegrationEvent>("communication.meeting.invitation_revoked.v1");

        Assert.Equal(InviteeId, evt.InviteeUserId);
        Assert.Equal("Customer", evt.InviteeKind);
        Assert.Equal(HostId, evt.RevokedByUserId);
        Assert.True(evt.Ejected);
    }

    private static T Deserialize<T>(string eventType)
    {
        var evt = JsonSerializer.Deserialize<T>(ReadFixture(eventType), Json);
        Assert.NotNull(evt);
        return evt;
    }

    private static string ReadFixture(string eventType)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(
            Path.Combine(
                directory!.FullName,
                "src",
                "BuildingBlocks",
                "Messaging",
                "Contracts",
                "Fixtures",
                $"{eventType}.json"
            )
        );
    }
}
