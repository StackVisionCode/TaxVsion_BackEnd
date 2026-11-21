namespace BuildingBlocks.Contracts.Files;

/// <summary>Pregunta al servicio dueño qué archivos puede ver quien llama. Lo que no ve se omite, sin error.</summary>
public sealed record FileVisibilityRequest(IReadOnlyList<Guid> FileIds, string? Purpose = null);

public sealed record FileVisibilityResponse(IReadOnlyList<VisibleFile> Items);

/// <summary>Un archivo visible y el recurso desde el que quien mira lo ve en el módulo.</summary>
/// <param name="CanDownload">false = quien mira ve la fila pero no puede bajar el archivo.</param>
/// <param name="RestrictedTo">
/// Marca de privacidad (<see cref="FileVisibilityRestrictions"/>): el archivo no lo ve todo el personal con el
/// permiso del módulo. null = sin marca.
/// </param>
public sealed record VisibleFile(
    Guid FileId,
    string ResourceType,
    Guid ResourceId,
    string? Title,
    Guid? CustomerId,
    IReadOnlyDictionary<string, string>? Context = null,
    bool CanDownload = true,
    string? RestrictedTo = null
);

public static class FileVisibilityLimits
{
    public const int Batch = 100;
}

public static class FileVisibilityPurposes
{
    public const string List = "list";
    public const string Download = "download";
}

/// <summary>Valores de <see cref="VisibleFile.RestrictedTo"/>.</summary>
public static class FileVisibilityRestrictions
{
    public const string OnlyYou = "only_you";
    public const string Participants = "participants";
    public const string PersonalMailbox = "personal_mailbox";
    public const string AdminView = "admin_view";
}

/// <summary>Tipos de recurso de la respuesta que no son del Work Profile.</summary>
public static class FileVisibilityResourceTypes
{
    public const string Conversation = "conversation";
    public const string SupportTicket = "support_ticket";
    public const string Meeting = "meeting";
    public const string Call = "call";
    public const string Note = "note";
}

/// <summary>
/// Conversaciones, llamadas y reuniones cuyos archivos puede ver quien llama. Es una optimización: cada archivo lo
/// sigue confirmando la visibilidad por archivo.
/// </summary>
/// <param name="Truncated">Listas cortadas por el tope (<see cref="CommunicationFileScopeLists"/>).</param>
public sealed record CommunicationFileScope(
    IReadOnlyList<Guid> ConversationIds,
    IReadOnlyList<Guid> CallIds,
    IReadOnlyList<Guid> MeetingIds,
    IReadOnlyList<string> Truncated
);

public static class CommunicationFileScopeLists
{
    public const string Conversations = "conversations";
    public const string Calls = "calls";
    public const string Meetings = "meetings";

    /// <summary>Tope de ids por lista; se quedan los de actividad más reciente.</summary>
    public const int MaxIds = 2000;
}
