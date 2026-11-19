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

/// <summary>Entrada del registro de proveedores; los paths llevan <c>{userId}</c> como marcador.</summary>
public sealed record WorkProviderDescriptor(string Service, string Module, string WorkPath, string BatchPath);

/// <summary>Límites del contrato.</summary>
public static class WorkLimits
{
    public const int TopItems = 5;
    public const int BatchUsers = 100;
}
