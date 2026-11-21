using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class StorageActorScopeOwnerMediatedTests
{
    [Fact]
    public void A_person_with_the_mark_cannot_reach_owner_mediated_files()
    {
        var scope = new StorageActorScope(false, null) { HidesOwnerMediated = true };

        Assert.False(scope.CanAccess(OwnerMediatedFiles.Create(OwnerType.Communication, FolderType.Other)));
        Assert.False(scope.CanAccess(OwnerMediatedFiles.Create(OwnerType.Tenant, FolderType.VoiceNotes)));
        Assert.True(scope.CanAccess(OwnerMediatedFiles.Create(OwnerType.Customer, FolderType.Documents)));
    }

    [Fact]
    public void A_service_reaches_owner_mediated_files()
    {
        var scope = new StorageActorScope(false, null) { IsService = true, HidesOwnerMediated = true };

        Assert.True(scope.CanAccess(OwnerMediatedFiles.Create(OwnerType.Communication, FolderType.Recordings)));
    }

    [Fact]
    public void Without_the_mark_nothing_changes()
    {
        var scope = new StorageActorScope(false, null);

        Assert.True(scope.CanAccess(OwnerMediatedFiles.Create(OwnerType.Communication, FolderType.Transcripts)));
        Assert.True(scope.CanAccess(OwnerMediatedFiles.Create(OwnerType.Tenant, FolderType.VoiceNotes)));
    }

    [Fact]
    public void A_person_with_the_mark_cannot_reach_the_voice_notes_folder()
    {
        var scope = new StorageActorScope(false, null) { HidesOwnerMediated = true };

        Assert.True(scope.HidesOwnerMediatedFolder(OwnerMediatedFiles.SystemFolder("Voice Notes", "sys.voicenotes")));
        Assert.False(scope.HidesOwnerMediatedFolder(OwnerMediatedFiles.SystemFolder("Documents", "sys.documents")));
    }

    [Fact]
    public void A_person_with_the_mark_cannot_open_the_voice_notes_folder_by_id()
    {
        var person = new StorageActorScope(false, null) { HidesOwnerMediated = true };
        var service = new StorageActorScope(false, null) { IsService = true, HidesOwnerMediated = true };
        var voiceNotes = OwnerMediatedFiles.SystemFolder("Voice Notes", "sys.voicenotes");

        Assert.False(person.CanAccess(voiceNotes));
        Assert.True(person.CanAccess(OwnerMediatedFiles.SystemFolder("Documents", "sys.documents")));
        Assert.True(service.CanAccess(voiceNotes));
    }
}
