namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition CommunicationMeetingJoinByToken = Define(
        "communication.d.meeting_join_by_token",
        RateLimitCategory.D,
        RateLimitPartitionDimension.Token,
        [RateLimitPartitionDimension.Ip],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );

    // Categoría O — valores reales, no del doc de diseño: ya viven en producción en
    // Communication (config.ts:73-92, COMMUNICATION_RATE_LIMIT_*). Se transcriben tal
    // cual, no se re-derivan.
    public static readonly RateLimitPolicyDefinition CommunicationChatSend = Define(
        "communication.o.chat_send",
        RateLimitCategory.O,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [],
        quota: 30,
        windowSeconds: 10,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition CommunicationChatEdit = Define(
        "communication.o.chat_edit",
        RateLimitCategory.O,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [],
        quota: 20,
        windowSeconds: 10,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition CommunicationChatTyping = Define(
        "communication.o.chat_typing",
        RateLimitCategory.O,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [],
        quota: 20,
        windowSeconds: 10,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition CommunicationCallInitiate = Define(
        "communication.o.call_initiate",
        RateLimitCategory.O,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [],
        quota: 10,
        windowSeconds: 30,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition CommunicationCallSignal = Define(
        "communication.o.call_signal",
        RateLimitCategory.O,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [],
        quota: 60,
        windowSeconds: 10,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition CommunicationMeetingChatSend = Define(
        "communication.o.meeting_chat_send",
        RateLimitCategory.O,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [],
        quota: 30,
        windowSeconds: 10,
        RateLimitAlgorithm.FixedWindow
    );
}
