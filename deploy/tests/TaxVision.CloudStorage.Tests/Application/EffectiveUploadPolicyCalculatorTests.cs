using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// La regla de qué se puede subir (antes en CloudStorageOptions + appsettings, ahora catálogo y carpetas en
/// BD + plan desde entitlements): carpeta ∩ grupos del plan ∩ restricción del portal − lista negra; tamaño,
/// el menor entre carpeta y plan salvo excepción en BD. Los casos son los mismos que cubría la regla vieja.
/// </summary>
public sealed class EffectiveUploadPolicyCalculatorTests
{
    private const long Mb = 1024 * 1024;

    private static readonly PlanFileAllowance Starter = new(
        PlanFileAllowanceDefaults.ParseGroups("documents,spreadsheets,images,data,media"),
        10 * Mb
    );

    private static readonly PlanFileAllowance Pro = new(
        PlanFileAllowanceDefaults.ParseGroups("documents,spreadsheets,images,data,media,presentations,rich_text,web_images,archives,xml"),
        25 * Mb
    );

    private static EffectiveUploadPolicy Resolve(
        FolderType folderType,
        PlanFileAllowance plan,
        long? sizeOverride = null,
        PortalUploadRestriction? portal = null
    ) => EffectiveUploadPolicyCalculator.Calculate(TestFilePolicyCatalog.Folder(folderType), TestFilePolicyCatalog.FileTypes(), plan, sizeOverride, portal);

    [Fact]
    public void An_extension_the_plan_sells_but_the_folder_does_not_list_is_rejected()
    {
        var policy = Resolve(FolderType.Imports, Pro);

        Assert.DoesNotContain(".pdf", policy.AllowedExtensions);
        Assert.Equal([".csv", ".xlsx", ".zip"], policy.AllowedExtensions.Order());
    }

    [Fact]
    public void A_dangerous_extension_never_gets_through_even_if_someone_lists_it()
    {
        // La propia política de carpeta rechaza darla de alta; el catálogo tampoco la admite.
        Assert.True(FolderFilePolicy.Create(FolderType.Backups, Mb, [".zip", ".exe"], true, true, Guid.Empty, DateTime.UtcNow).IsFailure);
        Assert.True(FileTypeDefinition.Create(".exe", "documents", ["application/x-msdownload"], Guid.Empty, DateTime.UtcNow).IsFailure);
    }

    [Fact]
    public void The_size_is_the_smaller_of_plan_and_folder()
    {
        Assert.Equal(10 * Mb, Resolve(FolderType.Tasks, Starter).MaxFileSizeBytes);
        Assert.Equal(5 * Mb, Resolve(FolderType.Templates, Pro).MaxFileSizeBytes);
    }

    [Fact]
    public void Starter_keeps_exactly_what_appsettings_gave_it_and_Pro_adds_its_extra_groups()
    {
        var starter = Resolve(FolderType.Documents, Starter).AllowedExtensions;
        var pro = Resolve(FolderType.Documents, Pro).AllowedExtensions;

        Assert.DoesNotContain(".pptx", starter);
        Assert.DoesNotContain(".zip", starter);
        Assert.DoesNotContain(".rtf", starter);
        Assert.Contains(".pdf", starter);
        Assert.Contains(".pptx", pro);
        Assert.Contains(".zip", pro);
        Assert.Contains(".rtf", pro);
    }

    [Fact]
    public void Avatars_ignore_the_plan_so_webp_passes_on_a_plan_without_it_and_keep_5MB_images()
    {
        var policy = Resolve(FolderType.Avatars, Starter);

        Assert.Contains(".webp", policy.AllowedExtensions);
        Assert.Contains("image/webp", policy.AllowedContentTypes);
        Assert.Equal(5 * Mb, policy.MaxFileSizeBytes);
        Assert.Equal([".jpeg", ".jpg", ".png", ".webp"], policy.AllowedExtensions.Order());
    }

    [Fact]
    public void Recordings_and_voice_notes_accept_audio_even_though_the_plan_does_not_sell_it()
    {
        var recordings = Resolve(FolderType.Recordings, Starter);
        var voiceNotes = Resolve(FolderType.VoiceNotes, Starter);
        var tracks = Resolve(FolderType.RecordingTracks, Starter);

        Assert.Contains("audio/webm", recordings.AllowedContentTypes);
        Assert.Contains("audio/mp4", voiceNotes.AllowedContentTypes);
        Assert.Equal([".webm"], tracks.AllowedExtensions);
        Assert.Contains("audio/webm", tracks.AllowedContentTypes);
    }

    [Fact]
    public void Recording_folders_do_not_use_the_document_size_of_the_plan()
    {
        Assert.Equal(500 * Mb, Resolve(FolderType.Recordings, Starter).MaxFileSizeBytes);
        Assert.Equal(500 * Mb, Resolve(FolderType.RecordingTracks, Starter).MaxFileSizeBytes);
    }

    [Fact]
    public void A_database_size_override_wins_even_above_the_folder_ceiling()
    {
        Assert.Equal(2048 * Mb, Resolve(FolderType.Recordings, Starter, sizeOverride: 2048 * Mb).MaxFileSizeBytes);
    }

    [Fact]
    public void Regular_attachments_do_not_accept_audio()
    {
        var other = Resolve(FolderType.Other, Pro);

        Assert.DoesNotContain(".webm", other.AllowedExtensions);
        Assert.DoesNotContain("audio/webm", other.AllowedContentTypes);
    }

    [Fact]
    public void Transcripts_accept_txt_and_json()
    {
        Assert.Equal([".json", ".txt"], Resolve(FolderType.Transcripts, Starter).AllowedExtensions.Order());
    }

    [Fact]
    public void Branding_allows_svg_although_no_plan_sells_it()
    {
        var policy = Resolve(FolderType.Branding, Starter);

        Assert.Contains(".svg", policy.AllowedExtensions);
        Assert.Equal(500 * 1024, policy.MaxFileSizeBytes);
    }

    [Fact]
    public void Client_forms_use_their_own_list_and_the_plan_limit()
    {
        var policy = Resolve(FolderType.ClientForms, Starter);

        Assert.Equal(10 * Mb, policy.MaxFileSizeBytes);
        Assert.Contains(".pdf", policy.AllowedExtensions);
        Assert.DoesNotContain(".zip", policy.AllowedExtensions);
        Assert.False(FolderType.ClientForms.RequiresYear());
        Assert.Equal("client-forms", FolderType.ClientForms.ToSegment());
    }

    [Fact]
    public void The_office_can_narrow_what_its_clients_upload_from_the_portal()
    {
        var portal = new PortalUploadRestriction(PlanFileAllowanceDefaults.ParseGroups("documents,images"), 2 * Mb);

        var policy = Resolve(FolderType.Documents, Pro, portal: portal);

        Assert.Contains(".pdf", policy.AllowedExtensions);
        Assert.Contains(".png", policy.AllowedExtensions);
        Assert.DoesNotContain(".xlsx", policy.AllowedExtensions);
        Assert.Equal(2 * Mb, policy.MaxFileSizeBytes);
    }

    [Fact]
    public void A_disabled_catalog_type_is_not_uploadable_anywhere()
    {
        var catalog = TestFilePolicyCatalog.FileTypes();
        var pdf = catalog.Single(definition => definition.Extension == ".pdf");
        pdf.Update(pdf.Group, pdf.ContentTypeList.ToList(), enabled: false, Guid.Empty, DateTime.UtcNow);

        var policy = EffectiveUploadPolicyCalculator.Calculate(TestFilePolicyCatalog.Folder(FolderType.Documents), catalog, Pro, null, null);

        Assert.DoesNotContain(".pdf", policy.AllowedExtensions);
        Assert.DoesNotContain("application/pdf", policy.AllowedContentTypes);
    }

    [Fact]
    public void Voice_notes_is_a_navigable_system_folder()
    {
        Assert.True(TaxVision.CloudStorage.Application.Folders.SystemFolderCatalog.IsNavigable(FolderType.VoiceNotes));
        Assert.True(TaxVision.CloudStorage.Application.Folders.SystemFolderCatalog.TryGet(FolderType.VoiceNotes, out var spec));
        Assert.Equal("Voice Notes", spec.Name);
        Assert.Equal("sys.voicenotes", spec.Category);
    }
}
