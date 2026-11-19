namespace BuildingBlocks.Contracts.Work;

/// <summary>
/// Trabajo a cargo de una persona en un servicio (<c>GET /{svc}/work/{userId}</c>). Cada servicio dueño lo
/// calcula con su propio ABAC para quien mira, conteos incluidos.
/// </summary>
public sealed record WorkSummaryResponse(
    string Provider,
    string Module,
    IReadOnlyList<WorkCount> Counts,
    IReadOnlyList<WorkItem> Top
);

/// <summary>Conteos de un tipo de trabajo. <see cref="Kind"/> sale de <see cref="WorkKinds"/>.</summary>
public sealed record WorkCount(string Kind, int Open, int Overdue, int DueSoon, int Waiting);

/// <summary>
/// Un elemento del top (máximo <see cref="WorkLimits.TopItems"/>). <see cref="DueDate"/> para vencimientos
/// civiles, <see cref="DueAtUtc"/> para instantes. <see cref="Link"/> es la ruta relativa del CRM.
/// </summary>
public sealed record WorkItem(
    Guid Id,
    string Kind,
    string Title,
    Guid? CustomerId,
    DateTime? DueAtUtc,
    DateOnly? DueDate,
    string Status,
    string Link
);

/// <summary>Conteos por persona para la tabla de miembros (<c>GET /{svc}/work/summary?userIds=</c>).</summary>
public sealed record WorkBatchResponse(IReadOnlyList<WorkBatchRow> Rows);

public sealed record WorkBatchRow(Guid UserId, int Open, int Overdue);

/// <summary>
/// Entrada del registro de proveedores; los paths llevan <c>{userId}</c> como marcador. <see cref="ItemsPath"/>:
/// lista paginada de un tipo (<c>GET {ItemsPath}?kind=&amp;page=&amp;size=</c> → <c>PagedResult&lt;WorkItem&gt;</c>);
/// null = el proveedor no la ofrece.
/// </summary>
public sealed record WorkProviderDescriptor(
    string Service,
    string Module,
    string WorkPath,
    string BatchPath,
    string? ItemsPath = null
);

/// <summary>
/// Títulos en vivo para el historial (<c>POST {Path}</c>). El historial guarda solo ids; quien lo muestra pide los
/// títulos al módulo dueño, que devuelve solo los que quien mira puede leer. Lo que falta no es un error.
/// </summary>
public sealed record WorkTitlesRequest(IReadOnlyList<WorkTitleRef> Items);

public sealed record WorkTitleRef(string ResourceType, Guid Id);

public sealed record WorkTitlesResponse(IReadOnlyList<WorkTitle> Items);

public sealed record WorkTitle(string ResourceType, Guid Id, string Title);

/// <summary>Entrada del registro de títulos: qué tipos de recurso resuelve cada servicio.</summary>
public sealed record TitleProviderDescriptor(string Service, IReadOnlyList<string> ResourceTypes, string Path);

/// <summary>Tipos de recurso del historial (contrato con el CRM, no se renombran).</summary>
public static class WorkResourceTypes
{
    public const string Task = "task";
    public const string ClientRequest = "client_request";
    public const string FormRequest = "form_request";
    public const string Appointment = "appointment";
    public const string Note = "note";
    public const string Customer = "customer";
    public const string SignatureRequest = "signature_request";
    public const string Invoice = "invoice";
    public const string SmsMessage = "sms_message";
    public const string EmailMessage = "email_message";
    public const string EmailThread = "email_thread";
    public const string Document = "document";
    public const string SupportTicket = "support_ticket";
}

/// <summary>Límites del contrato.</summary>
public static class WorkLimits
{
    public const int TopItems = 5;
    public const int BatchUsers = 100;
    public const int ItemsPageSize = 20;
    public const int ItemsMaxPageSize = 50;
    public const int TitleBatch = 100;
    public const int TitleMaxLength = 120;

    /// <summary>Recorta un título a <see cref="TitleMaxLength"/> con "…"; null o vacío → null.</summary>
    public static string? ClipTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;
        var clean = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= TitleMaxLength ? clean : string.Concat(clean.AsSpan(0, TitleMaxLength - 1), "…");
    }
}
