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

    // F8a (D-15): adjuntos del chat de la sala por minuto; el tope efectivo lo fija la configuración de Communication.
    public static readonly RateLimitPolicyDefinition CommunicationMeetingChatAttachment = Define(
        "communication.o.meeting_chat_attachment",
        RateLimitCategory.O,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [],
        quota: 5,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );

    // F11 (E-259): invitaciones a reuniones por persona (cada una puede ser un email externo); el tope efectivo
    // lo fija la configuración de Communication.
    public static readonly RateLimitPolicyDefinition CommunicationMeetingInvitationsCreate = Define(
        "communication.o.meeting_invitations_create",
        RateLimitCategory.O,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [],
        quota: 20,
        windowSeconds: 3600,
        RateLimitAlgorithm.FixedWindow
    );

    // F8d: reacciones del chat por persona; el tope efectivo lo fija la configuración de Communication.
    public static readonly RateLimitPolicyDefinition CommunicationChatReaction = Define(
        "communication.o.chat_reaction",
        RateLimitCategory.O,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );
}
