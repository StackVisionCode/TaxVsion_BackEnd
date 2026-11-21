using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class FileReadGateOwnerMediatedTests
{
    [Theory]
    [InlineData(FolderType.Other)]
    [InlineData(FolderType.Recordings)]
    [InlineData(FolderType.RecordingTracks)]
    [InlineData(FolderType.Transcripts)]
    public void Communication_files_are_owner_mediated(FolderType folderType)
    {
        var file = OwnerMediatedFiles.Create(OwnerType.Communication, folderType);

        Assert.True(FileReadGate.IsOwnerMediated(file));
    }

    [Fact]
    public void Group_photos_of_communication_are_not_owner_mediated()
    {
        var file = OwnerMediatedFiles.Create(OwnerType.Communication, FolderType.Avatars);

        Assert.False(FileReadGate.IsOwnerMediated(file));
    }

    [Theory]
    [InlineData(OwnerType.Tenant)]
    [InlineData(OwnerType.Communication)]
    public void Voice_notes_are_owner_mediated(OwnerType ownerType)
    {
        var file = OwnerMediatedFiles.Create(ownerType, FolderType.VoiceNotes);

        Assert.True(FileReadGate.IsOwnerMediated(file));
    }

    [Theory]
    [InlineData(FolderType.Documents)]
    [InlineData(FolderType.Invoices)]
    [InlineData(FolderType.Signatures)]
    public void Customer_files_are_not_owner_mediated(FolderType folderType)
    {
        var file = OwnerMediatedFiles.Create(OwnerType.Customer, folderType);

        Assert.False(FileReadGate.IsOwnerMediated(file));
    }

    [Fact]
    public void Only_the_voice_notes_system_folder_is_owner_mediated()
    {
        Assert.True(FileReadGate.IsOwnerMediated(OwnerMediatedFiles.SystemFolder("Voice Notes", "sys.voicenotes")));
        Assert.False(FileReadGate.IsOwnerMediated(OwnerMediatedFiles.SystemFolder("Documents", "sys.documents")));
    }
}

/// <summary>Archivos y carpetas de prueba para la compuerta "solo por su dueño".</summary>
internal static class OwnerMediatedFiles
{
    public static readonly Guid Tenant = Guid.NewGuid();

    public static FileObject Create(
        OwnerType ownerType,
        FolderType folderType,
        bool available = false,
        Guid? folderId = null,
        DateTime? createdAtUtc = null
    )
    {
        var now = createdAtUtc ?? DateTime.UtcNow;
        var fileId = Guid.NewGuid();
        Guid? ownerId = ownerType == OwnerType.Tenant ? null : Guid.NewGuid();
        var key = ObjectKey
            .Create($"tenants/{Tenant:N}/{ownerType.ToSegment()}/{folderType.ToSegment()}/{fileId:N}.webm")
            .Value;
        var file = FileObject
            .Register(
                fileId,
                Tenant,
                ownerType,
                ownerId,
                folderType,
                folderType.RequiresYear() ? 2025 : null,
                key,
                "file.webm",
                "audio/webm",
                10,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
        if (folderId is { } folder)
            file.MoveToFolder(folder, now);
        if (available)
        {
            file.MarkPendingScan(now);
            file.MarkScanning();
            file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "audio/webm", now);
        }
        return file;
    }

    public static Folder SystemFolder(string name, string category) =>
        Folder
            .Create(
                Guid.NewGuid(),
                Tenant,
                OwnerType.Tenant,
                null,
                null,
                FolderName.Create(name).Value,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                FolderCategory.Create(category).Value
            )
            .Value;
}
