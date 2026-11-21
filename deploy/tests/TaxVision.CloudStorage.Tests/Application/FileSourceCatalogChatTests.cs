using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Pares de chat que declara Communication: cada papel con su dueño y su carpeta.</summary>
public sealed class FileSourceCatalogChatTests
{
    [Theory]
    [InlineData("attachment", OwnerType.Communication, FolderType.Other)]
    [InlineData("voice_note", OwnerType.Communication, FolderType.VoiceNotes)]
    [InlineData("voice_note", OwnerType.Tenant, FolderType.VoiceNotes)]
    public void Chat_pairs_on_their_owner_and_folder_are_accepted(string role, OwnerType owner, FolderType folder)
    {
        Assert.True(FileSourceCatalog.Accepts("conversation", role, owner, folder));
    }

    [Theory]
    [InlineData("attachment", OwnerType.Customer, FolderType.Documents)]
    [InlineData("attachment", OwnerType.Tenant, FolderType.Other)]
    [InlineData("attachment", OwnerType.Communication, FolderType.VoiceNotes)]
    [InlineData("attachment", OwnerType.Communication, FolderType.Transcripts)]
    [InlineData("voice_note", OwnerType.Communication, FolderType.Other)]
    [InlineData("voice_note", OwnerType.Customer, FolderType.VoiceNotes)]
    [InlineData("recording", OwnerType.Communication, FolderType.Other)]
    public void A_chat_declaration_on_another_owner_or_folder_is_rejected(
        string role,
        OwnerType owner,
        FolderType folder
    )
    {
        Assert.False(FileSourceCatalog.Accepts("conversation", role, owner, folder));
    }

    [Fact]
    public void Conversations_belong_to_communication()
    {
        Assert.Equal("communication", FileSourceCatalog.ServiceOf("conversation"));
    }
}
