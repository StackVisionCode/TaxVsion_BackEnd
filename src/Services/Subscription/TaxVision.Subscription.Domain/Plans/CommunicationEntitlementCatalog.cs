using System.Globalization;
using TaxVision.Subscription.Domain.Entitlements;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Claves <c>communication.*</c> que todo plan comercial debe traer en su versión publicada. Las lee
/// Communication (Node) del snapshot <c>entitlementValues</c> de <c>subscription.entitlements_changed</c>;
/// hasta ahora ningún plan las definía y el servicio corría con sus fallbacks internos. Los valores
/// replican esos fallbacks (sin cambio comercial) salvo los dos fijados por el dueño: 60 min por
/// reunión y 10 participantes. Única fuente de estas claves: el seeder y el reconciler leen de aquí.
/// </summary>
public static class CommunicationEntitlementCatalog
{
    public const string MaxParticipantsPerMeeting = "communication.max_participants_per_meeting";
    public const string MaxMeetingMinutes = "communication.max_meeting_minutes";
    public const string MaxConcurrentCalls = "communication.max_concurrent_calls";
    public const string MaxMonthlyMinutes = "communication.max_monthly_minutes";
    public const string CallsEnabled = "communication.calls_enabled";
    public const string MaxCallMinutes = "communication.max_call_minutes";
    public const string SupportChatEnabled = "communication.support_chat_enabled";
    public const string RecordingEnabled = "communication.recording_enabled";
    public const string RecordingVideoEnabled = "communication.recording_video_enabled";
    public const string TranscriptEnabled = "communication.transcript_enabled";
    public const string MaxRecordingMinutesMeeting = "communication.max_recording_minutes.meeting";
    public const string MaxRecordingMinutesAudioCall = "communication.max_recording_minutes.audio_call";
    public const string MaxRecordingMinutesVideoCall = "communication.max_recording_minutes.video_call";

    public static IReadOnlyList<string> Keys { get; } =
    [
        MaxParticipantsPerMeeting,
        MaxMeetingMinutes,
        MaxConcurrentCalls,
        MaxMonthlyMinutes,
        CallsEnabled,
        MaxCallMinutes,
        SupportChatEnabled,
        RecordingEnabled,
        RecordingVideoEnabled,
        TranscriptEnabled,
        MaxRecordingMinutesMeeting,
        MaxRecordingMinutesAudioCall,
        MaxRecordingMinutesVideoCall,
    ];

    /// <summary>Semillas completas del tier. Solo la grabación de reuniones varía por plan (Starter 30 min).</summary>
    public static IReadOnlyCollection<EntitlementSeed> SeedsFor(PlanTier tier) =>
        [
            Int(MaxParticipantsPerMeeting, 10),
            Int(MaxMeetingMinutes, 60),
            Int(MaxConcurrentCalls, 2),
            Int(MaxMonthlyMinutes, 600),
            Bool(CallsEnabled, true),
            Int(MaxCallMinutes, 60),
            Bool(SupportChatEnabled, true),
            Bool(RecordingEnabled, true),
            Bool(RecordingVideoEnabled, true),
            Bool(TranscriptEnabled, true),
            Int(MaxRecordingMinutesMeeting, tier is PlanTier.Pro or PlanTier.Enterprise ? 60 : 30),
            Int(MaxRecordingMinutesAudioCall, 60),
            Int(MaxRecordingMinutesVideoCall, 60),
        ];

    /// <summary>Semillas del tier que la versión todavía no define (las existentes nunca se tocan).</summary>
    public static IReadOnlyCollection<EntitlementSeed> MissingFrom(SubscriptionPlanVersion version, PlanTier tier)
    {
        var present = version
            .Entitlements.Select(entitlement => entitlement.Key.Value)
            .ToHashSet(StringComparer.Ordinal);
        return SeedsFor(tier).Where(seed => !present.Contains(seed.Key)).ToArray();
    }

    private static EntitlementSeed Int(string key, int value) =>
        new(key, EntitlementValueType.Int, value.ToString(CultureInfo.InvariantCulture));

    // "True"/"False": mismo formato que produce el recálculo para las features (bool.ToString()).
    private static EntitlementSeed Bool(string key, bool value) =>
        new(key, EntitlementValueType.Bool, value.ToString());
}
