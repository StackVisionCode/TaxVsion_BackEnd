using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Pares de grabaciones que declara Communication: la grabación en Recordings y los transcripts en Transcripts.</summary>
public sealed class FileSourceCatalogRecordingsTests
{
    [Theory]
    [InlineData("call", "recording", FolderType.Recordings)]
    [InlineData("meeting", "recording", FolderType.Recordings)]
    [InlineData("call", "transcript", FolderType.Transcripts)]
    [InlineData("meeting", "transcript", FolderType.Transcripts)]
    [InlineData("call", "transcript_data", FolderType.Transcripts)]
    [InlineData("meeting", "transcript_data", FolderType.Transcripts)]
    public void Recording_pairs_on_communication_and_their_folder_are_accepted(
        string resourceType,
        string role,
        FolderType folder
    )
    {
        Assert.True(FileSourceCatalog.Accepts(resourceType, role, OwnerType.Communication, folder));
    }

    [Theory]
    [InlineData("call", "recording", OwnerType.Communication, FolderType.Transcripts)]
    [InlineData("meeting", "transcript", OwnerType.Communication, FolderType.Recordings)]
    [InlineData("call", "recording", OwnerType.Communication, FolderType.RecordingTracks)]
    [InlineData("call", "recording", OwnerType.Customer, FolderType.Recordings)]
    [InlineData("meeting", "transcript", OwnerType.Communication, FolderType.Other)]
    [InlineData("call", "attachment", OwnerType.Communication, FolderType.Other)]
    public void A_recording_declaration_on_another_owner_or_folder_is_rejected(
        string resourceType,
        string role,
        OwnerType owner,
        FolderType folder
    )
    {
        Assert.False(FileSourceCatalog.Accepts(resourceType, role, owner, folder));
    }

    [Theory]
    [InlineData("call")]
    [InlineData("meeting")]
    public void Calls_and_meetings_belong_to_communication(string resourceType)
    {
        Assert.Equal("communication", FileSourceCatalog.ServiceOf(resourceType));
    }
}
