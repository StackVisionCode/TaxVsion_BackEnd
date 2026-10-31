using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Moderation;

namespace TaxVision.CloudStorage.Tests.Domain;

/// <summary>Umbrales, alcance (tipo detectado + carpetas) y validación de la política de moderación (T-20).</summary>
public sealed class ContentModerationPolicyTests
{
    internal static ContentModerationSettings Strict(
        bool enabled = true,
        bool scanVideos = true,
        bool failClosed = true,
        long maxScanBytes = 256L * 1024 * 1024
    ) =>
        new(
            enabled,
            BlockThreshold: 0.90,
            ReviewThreshold: 0.60,
            scanVideos,
            FrameIntervalSeconds: 2,
            MaxFrames: 30,
            maxScanBytes,
            [FolderType.Recordings, FolderType.RecordingTracks, FolderType.Transcripts],
            failClosed
        );

    [Theory]
    [InlineData(0.95, ModerationDecision.Block)]
    [InlineData(0.90, ModerationDecision.Block)]
    [InlineData(0.75, ModerationDecision.Review)]
    [InlineData(0.60, ModerationDecision.Review)]
    [InlineData(0.10, ModerationDecision.Clean)]
    public void The_score_maps_to_a_decision_with_inclusive_thresholds(double score, ModerationDecision expected) =>
        Assert.Equal(expected, Strict().Decide(score));

    [Theory]
    [InlineData(FolderType.Documents, "image/jpeg", true)]
    [InlineData(FolderType.Avatars, "image/webp", true)]
    [InlineData(FolderType.Other, "video/mp4", true)]
    [InlineData(FolderType.Documents, "application/pdf", false)]
    [InlineData(FolderType.VoiceNotes, "audio/webm", false)]
    [InlineData(FolderType.Recordings, "video/webm", false)]
    [InlineData(FolderType.Documents, "", false)]
    public void Only_images_and_videos_outside_excluded_folders_are_scanned(
        FolderType folder,
        string contentType,
        bool expected
    ) => Assert.Equal(expected, Strict().AppliesTo(folder, contentType));

    [Fact]
    public void Videos_are_skipped_when_video_scanning_is_off_and_nothing_applies_when_disabled()
    {
        Assert.False(Strict(scanVideos: false).AppliesTo(FolderType.Other, "video/mp4"));
        Assert.True(Strict(scanVideos: false).AppliesTo(FolderType.Other, "image/png"));
        Assert.False(Strict(enabled: false).AppliesTo(FolderType.Other, "image/png"));
    }

    [Theory]
    [InlineData(0.6, 0.9, 2, 30, true)]
    [InlineData(0.9, 0.9, 1, 1, true)]
    [InlineData(0.95, 0.9, 2, 30, false)] // revisión por encima del bloqueo
    [InlineData(0, 0.9, 2, 30, false)]
    [InlineData(0.6, 1.1, 2, 30, false)]
    [InlineData(0.6, 0.9, 0, 30, false)]
    [InlineData(0.6, 0.9, 61, 30, false)]
    [InlineData(0.6, 0.9, 2, 0, false)]
    [InlineData(0.6, 0.9, 2, 121, false)]
    public void Settings_are_validated_together(double review, double block, int interval, int frames, bool valid)
    {
        var settings = Strict() with
        {
            ReviewThreshold = review,
            BlockThreshold = block,
            FrameIntervalSeconds = interval,
            MaxFrames = frames,
        };
        Assert.Equal(valid, ContentModerationPolicy.Validate(settings).IsSuccess);
    }

    [Fact]
    public void Update_round_trips_the_excluded_folders_and_rejects_invalid_values()
    {
        var actor = Guid.NewGuid();
        var policy = ContentModerationPolicy.Create(Strict(), actor, DateTime.UtcNow).Value;

        Assert.Equal(ContentModerationPolicy.PlatformPolicyId, policy.Id);
        Assert.Equal("RecordingTracks,Recordings,Transcripts", policy.ExcludedFolders);
        Assert.Contains(FolderType.Recordings, policy.ToSettings().ExcludedFolders);

        var invalid = policy.Update(Strict() with { BlockThreshold = 0.5 }, actor, DateTime.UtcNow);
        Assert.Equal(ContentModerationErrors.InvalidThresholds, invalid.Error);
        Assert.Equal(0.90, policy.BlockThreshold);
    }
}
