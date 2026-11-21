using System.Text.Json;
using BuildingBlocks.Contracts.Files;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Contracts;

public sealed class FileVisibilityContractsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void The_request_travels_in_camel_case_and_purpose_is_optional()
    {
        var fileId = Guid.NewGuid();

        var request = JsonSerializer.Deserialize<FileVisibilityRequest>($$"""{"fileIds":["{{fileId}}"]}""", Web)!;

        Assert.Equal([fileId], request.FileIds);
        Assert.Null(request.Purpose);
    }

    [Fact]
    public void The_response_round_trips()
    {
        var item = new VisibleFile(
            Guid.NewGuid(),
            "email_thread",
            Guid.NewGuid(),
            "Tax return",
            Guid.NewGuid(),
            new Dictionary<string, string> { ["messageId"] = Guid.NewGuid().ToString() }
        );

        var json = JsonSerializer.Serialize(new FileVisibilityResponse([item]), Web);
        var back = JsonSerializer.Deserialize<FileVisibilityResponse>(json, Web)!;

        Assert.Contains("\"resourceType\":\"email_thread\"", json);
        var single = Assert.Single(back.Items);
        Assert.Equal(item.FileId, single.FileId);
        Assert.Equal(item.Context!["messageId"], single.Context!["messageId"]);
    }

    [Fact]
    public void A_visible_file_without_the_new_fields_can_be_downloaded_and_has_no_restriction()
    {
        var fileId = Guid.NewGuid();
        var json = $$"""{"items":[{"fileId":"{{fileId}}","resourceType":"task","resourceId":"{{Guid.NewGuid()}}"}]}""";

        var item = Assert.Single(JsonSerializer.Deserialize<FileVisibilityResponse>(json, Web)!.Items);

        Assert.True(item.CanDownload);
        Assert.Null(item.RestrictedTo);
        Assert.True(new VisibleFile(fileId, "task", Guid.NewGuid(), null, null).CanDownload);
    }

    [Fact]
    public void The_new_fields_travel_in_camel_case()
    {
        var item = new VisibleFile(
            Guid.NewGuid(),
            FileVisibilityResourceTypes.Note,
            Guid.NewGuid(),
            "Call notes",
            null,
            CanDownload: false,
            RestrictedTo: FileVisibilityRestrictions.OnlyYou
        );

        var json = JsonSerializer.Serialize(item, Web);
        var back = JsonSerializer.Deserialize<VisibleFile>(json, Web)!;

        Assert.Contains("\"canDownload\":false", json);
        Assert.Contains("\"restrictedTo\":\"only_you\"", json);
        Assert.False(back.CanDownload);
        Assert.Equal("only_you", back.RestrictedTo);
    }

    [Fact]
    public void Restrictions_and_new_resource_types_are_fixed()
    {
        Assert.Equal("only_you", FileVisibilityRestrictions.OnlyYou);
        Assert.Equal("participants", FileVisibilityRestrictions.Participants);
        Assert.Equal("personal_mailbox", FileVisibilityRestrictions.PersonalMailbox);
        Assert.Equal("admin_view", FileVisibilityRestrictions.AdminView);
        Assert.Equal(
            ["conversation", "support_ticket", "meeting", "call", "note"],
            [
                FileVisibilityResourceTypes.Conversation,
                FileVisibilityResourceTypes.SupportTicket,
                FileVisibilityResourceTypes.Meeting,
                FileVisibilityResourceTypes.Call,
                FileVisibilityResourceTypes.Note,
            ]
        );
    }

    [Fact]
    public void The_communication_scope_round_trips_with_its_list_names()
    {
        var conversation = Guid.NewGuid();
        var json =
            $$"""{"conversationIds":["{{conversation}}"],"callIds":[],"meetingIds":[],"truncated":["conversations"]}""";

        var scope = JsonSerializer.Deserialize<CommunicationFileScope>(json, Web)!;

        Assert.Equal([conversation], scope.ConversationIds);
        Assert.Empty(scope.CallIds);
        Assert.Empty(scope.MeetingIds);
        Assert.Equal([CommunicationFileScopeLists.Conversations], scope.Truncated);
        Assert.Equal("calls", CommunicationFileScopeLists.Calls);
        Assert.Equal("meetings", CommunicationFileScopeLists.Meetings);
        Assert.Equal(2000, CommunicationFileScopeLists.MaxIds);
    }

    [Fact]
    public void Limits_and_purposes_are_fixed()
    {
        Assert.Equal(100, FileVisibilityLimits.Batch);
        Assert.Equal("list", FileVisibilityPurposes.List);
        Assert.Equal("download", FileVisibilityPurposes.Download);
    }
}
