using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fase L1.1 — whitelist granular por FolderType + blacklist global de extensiones peligrosas.</summary>
public sealed class CloudStorageOptionsTests
{
    private static CloudStorageOptions Options() => new();

    [Fact]
    public void ResolveUploadPolicy_rejects_an_extension_allowed_by_the_plan_but_not_by_the_FolderType()
    {
        var options = Options();

        // .pdf esta en el default global del plan, pero Avatars solo acepta imagenes.
        var policy = options.ResolveUploadPolicy("unconfigured-plan", FolderType.Avatars);

        Assert.DoesNotContain(".pdf", policy.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(".jpg", policy.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveUploadPolicy_never_lets_a_dangerous_extension_through_even_if_a_FolderType_lists_it()
    {
        var options = Options();
        options.FolderTypePolicies[FolderType.Backups].AllowedExtensions = [".zip", ".exe"]; // misconfiguracion simulada

        var policy = options.ResolveUploadPolicy("unconfigured-plan", FolderType.Backups);

        Assert.DoesNotContain(".exe", policy.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(".zip", policy.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveUploadPolicy_caps_MaxFileSizeBytes_at_the_smaller_of_plan_and_FolderType()
    {
        var options = Options();
        options.PlanPolicies["starter"] = new StoragePlanPolicy
        {
            MaxFileSizeBytes = 10L * 1024 * 1024,
            AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"],
            AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"],
        };

        // Avatars por si solo permite 5 MB — mas chico que el plan starter (10 MB).
        var policy = options.ResolveUploadPolicy("starter", FolderType.Avatars);

        Assert.Equal(5L * 1024 * 1024, policy.MaxFileSizeBytes);
    }

    [Fact]
    public void ResolveUploadPolicy_only_allows_recording_formats_on_the_Recordings_FolderType()
    {
        var options = Options();

        var recordings = options.ResolveUploadPolicy("unconfigured-plan", FolderType.Recordings);
        var avatars = options.ResolveUploadPolicy("unconfigured-plan", FolderType.Avatars);

        Assert.Contains(".webm", recordings.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(".mp4", recordings.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(".webm", avatars.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(".mp4", avatars.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveUploadPolicy_uses_the_plan_FolderOverridesBytes_for_Recordings_instead_of_the_generic_MaxFileSizeBytes()
    {
        var options = Options();
        options.PlanPolicies["starter"] = new StoragePlanPolicy
        {
            MaxFileSizeBytes = 10L * 1024 * 1024,
            AllowedExtensions = [".webm", ".mp4"],
            AllowedContentTypes = ["video/webm", "video/mp4"],
            FolderOverridesBytes = new(StringComparer.OrdinalIgnoreCase) { ["Recordings"] = 150L * 1024 * 1024 },
        };

        var recordings = options.ResolveUploadPolicy("starter", FolderType.Recordings);
        var documents = options.ResolveUploadPolicy("starter", FolderType.Documents);

        Assert.Equal(150L * 1024 * 1024, recordings.MaxFileSizeBytes);
        Assert.Equal(10L * 1024 * 1024, documents.MaxFileSizeBytes);
    }

    [Fact]
    public void ResolveUploadPolicy_still_caps_the_FolderOverridesBytes_at_the_FolderType_hard_ceiling()
    {
        var options = Options();
        options.PlanPolicies["enterprise"] = new StoragePlanPolicy
        {
            MaxFileSizeBytes = 25L * 1024 * 1024,
            AllowedExtensions = [".webm", ".mp4"],
            AllowedContentTypes = ["video/webm", "video/mp4"],
            // Recordings solo permite hasta 500MB por FolderType — un override de plan mas alto no debe saltarselo.
            FolderOverridesBytes = new(StringComparer.OrdinalIgnoreCase) { ["Recordings"] = 1024L * 1024 * 1024 },
        };

        var policy = options.ResolveUploadPolicy("enterprise", FolderType.Recordings);

        Assert.Equal(500L * 1024 * 1024, policy.MaxFileSizeBytes);
    }

    [Fact]
    public void ResolveUploadPolicy_falls_back_to_DefaultMaxRecordingSizeBytes_for_an_unconfigured_plan()
    {
        var options = Options();

        var policy = options.ResolveUploadPolicy("unconfigured-plan", FolderType.Recordings);

        Assert.Equal(options.DefaultMaxRecordingSizeBytes, policy.MaxFileSizeBytes);
    }

    [Fact]
    public void ResolveUploadPolicy_allows_a_txt_transcript_on_the_Transcripts_FolderType()
    {
        var options = Options();

        // CommunicationTranscriptWorker sube el .txt de whisper.cpp con FolderType=Transcripts
        // (antes usaba Recordings por error, y RecordingsPolicy solo permite .webm/.mp4 — el
        // .txt quedaba rechazado siempre por whitelist, ver save-file-requested-publisher.ts).
        var policy = options.ResolveUploadPolicy("unconfigured-plan", FolderType.Transcripts);

        Assert.Contains(".txt", policy.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(".webm", policy.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveUploadPolicy_allows_audio_voice_notes_even_when_the_plan_does_not_list_audio()
    {
        var options = Options();

        // "unconfigured-plan" usa los defaults globales del plan, que NO listan audio/*. VoiceNotes es un
        // upload de SISTEMA (bypass como Branding), asi que su folder policy define el set sin intersectar
        // con el plan → el audio pasa igual. Sin el bypass, la interseccion dejaria fuera audio/webm.
        var voice = options.ResolveUploadPolicy("unconfigured-plan", FolderType.VoiceNotes);

        Assert.Contains(".webm", voice.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(".mp4", voice.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("audio/webm", voice.AllowedContentTypes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("audio/mp4", voice.AllowedContentTypes, StringComparer.OrdinalIgnoreCase);
        // El inspector sniffea webm→video/webm y mp4→video/mp4: los detectados tambien deben estar permitidos.
        Assert.Contains("video/webm", voice.AllowedContentTypes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("video/mp4", voice.AllowedContentTypes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveUploadPolicy_allows_audio_recordings_even_when_the_plan_does_not_list_audio()
    {
        var options = Options();
        // La grabacion de una call/meeting de SOLO-AUDIO se sube como audio/webm. Recordings es media de
        // SISTEMA (solo llega via el servidor de Communication), asi que su SET de tipos no se intersecta
        // con el plan — sin esto la interseccion quitaba audio/* y el InitiateUpload devolvia 400. El plan
        // "starter" no lista audio/* y ademas cap-ea Recordings a 150MB por FolderOverridesBytes.
        options.PlanPolicies["starter"] = new StoragePlanPolicy
        {
            MaxFileSizeBytes = 10L * 1024 * 1024,
            AllowedExtensions = [".webm", ".mp4"],
            AllowedContentTypes = ["video/webm", "video/mp4"],
            FolderOverridesBytes = new(StringComparer.OrdinalIgnoreCase) { ["Recordings"] = 150L * 1024 * 1024 },
        };

        var recordings = options.ResolveUploadPolicy("starter", FolderType.Recordings);

        // El gating de TIPO se exceptua (audio/* pasa aunque el plan no lo liste)...
        Assert.Contains("audio/webm", recordings.AllowedContentTypes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("audio/mp4", recordings.AllowedContentTypes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("video/webm", recordings.AllowedContentTypes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(".webm", recordings.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
        // ...pero el TAMANO SIGUE respetando el override por plan (no es un bypass total como VoiceNotes).
        Assert.Equal(150L * 1024 * 1024, recordings.MaxFileSizeBytes);
    }

    [Fact]
    public void ResolveUploadPolicy_does_not_allow_audio_on_the_Other_FolderType_used_by_regular_attachments()
    {
        var options = Options();

        // Guarda de regresion: los adjuntos normales del chat (FolderType.Other) NO deben aceptar audio;
        // solo las notas de voz (FolderType.VoiceNotes) lo permiten.
        var other = options.ResolveUploadPolicy("unconfigured-plan", FolderType.Other);

        Assert.DoesNotContain(".webm", other.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("audio/webm", other.AllowedContentTypes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void VoiceNotes_is_a_navigable_system_folder()
    {
        // Decision A: las notas de voz aparecen como carpeta navegable "Voice Notes" en el gestor.
        Assert.True(TaxVision.CloudStorage.Application.Folders.SystemFolderCatalog.IsNavigable(FolderType.VoiceNotes));
        Assert.True(
            TaxVision.CloudStorage.Application.Folders.SystemFolderCatalog.TryGet(FolderType.VoiceNotes, out var spec)
        );
        Assert.Equal("Voice Notes", spec.Name);
        Assert.Equal("sys.voicenotes", spec.Category);
    }

    [Fact]
    public void ResolveFolderTypePolicy_falls_back_to_Other_when_a_FolderType_has_no_entry()
    {
        var options = Options();
        options.FolderTypePolicies.Remove(FolderType.Backups);

        var fallback = options.ResolveFolderTypePolicy(FolderType.Backups);
        var other = options.ResolveFolderTypePolicy(FolderType.Other);

        Assert.Equal(other.MaxSizeBytes, fallback.MaxSizeBytes);
        Assert.Equal(other.AllowedExtensions, fallback.AllowedExtensions);
    }
}
