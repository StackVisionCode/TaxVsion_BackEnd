using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.TimeZones;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Queries.Work;

/// <summary>Fila mínima de una solicitud para medir trabajo; <see cref="CustomerId"/> = cliente principal.</summary>
public sealed record SignatureWorkRow(
    Guid Id,
    string Title,
    SignatureRequestStatus Status,
    Guid CreatedByUserId,
    Guid? PreparerUserId,
    bool PreparerSigned,
    DateTime? ExpiresAtUtc,
    Guid? CustomerId
);

/// <summary>Id y título de una solicitud visible para quien mira (títulos en vivo del historial).</summary>
public sealed record SignatureTitleRow(Guid Id, string Title);

/// <summary>Instantes de corte con la zona de la oficina: ahora, fin de "dueSoon" y desde cuándo cuenta una expirada.</summary>
public sealed record SignatureWorkWindow(DateTime NowUtc, DateTime DueSoonEndUtc, DateTime ExpiredSinceUtc)
{
    /// <summary>Una expirada sin completar sigue como vencida este número de días de la oficina.</summary>
    public const int RecentlyExpiredDays = 30;

    public static SignatureWorkWindow For(DateTime nowUtc, ResolvedTimeZone zone)
    {
        var today = CivilDate.Of(nowUtc, zone);
        return new SignatureWorkWindow(
            nowUtc,
            LocalDay.StartUtc(WorkDue.DueSoonEnd(today), zone),
            LocalDay.StartUtc(today.AddDays(-RecentlyExpiredDays), zone)
        );
    }
}

/// <summary>
/// Trabajo de firmas de una persona. open = enviadas que esperan su firma de preparador; waiting = enviadas por
/// ella (autora) que esperan a los firmantes; overdue = de esas, las pasadas de su expiración o expiradas hace poco;
/// dueSoon = las que expiran dentro de los 7 días de la oficina.
/// </summary>
public static class SignatureWork
{
    public const string Provider = "signature";
    public const string Module = "signatures";

    private enum Role
    {
        None,
        Open,
        Waiting,
        Expired,
    }

    public static WorkSummaryResponse Summarize(
        Guid userId,
        IReadOnlyList<SignatureWorkRow> rows,
        SignatureWorkWindow window
    )
    {
        var mine = rows.Select(row => (Row: row, Role: RoleOf(row, userId)))
            .Where(item => item.Role != Role.None)
            .ToList();

        var count = new WorkCount(
            WorkKinds.Signatures,
            Open: mine.Count(item => item.Role == Role.Open),
            Overdue: mine.Count(item => IsOverdue(item.Row, item.Role, window)),
            DueSoon: mine.Count(item => IsDueSoon(item.Row, item.Role, window)),
            Waiting: mine.Count(item => item.Role == Role.Waiting)
        );

        var top = Ordered(mine, window).Take(WorkLimits.TopItems).Select(item => ToItem(item.Row, item.Role)).ToList();

        return new WorkSummaryResponse(Provider, Module, [count], top);
    }

    /// <summary>
    /// Página del mismo universo y orden que el top (abiertas, en espera y expiradas hace poco): la lista expandida
    /// siempre contiene lo que muestra el top. <c>TotalCount</c> = tamaño de ese universo.
    /// </summary>
    public static PagedResult<WorkItem> Items(
        Guid userId,
        IReadOnlyList<SignatureWorkRow> rows,
        SignatureWorkWindow window,
        int page,
        int size
    )
    {
        var mine = rows.Select(row => (Row: row, Role: RoleOf(row, userId)))
            .Where(item => item.Role != Role.None)
            .ToList();
        var items = Ordered(mine, window)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(item => ToItem(item.Row, item.Role))
            .ToList();
        return new PagedResult<WorkItem>(items, page, size, mine.Count);
    }

    /// <summary>Títulos de una línea y recortados; los vacíos se omiten.</summary>
    public static WorkTitlesResponse Titles(IReadOnlyList<SignatureTitleRow> rows) =>
        new(
            rows.Select(row => (row.Id, Title: WorkLimits.ClipTitle(row.Title)))
                .Where(row => row.Title is not null)
                .Select(row => new WorkTitle(WorkResourceTypes.SignatureRequest, row.Id, row.Title!))
                .ToList()
        );

    // Más urgentes primero: vencidas, próximas, el resto por expiración; sin expiración al final.
    private static IOrderedEnumerable<(SignatureWorkRow Row, Role Role)> Ordered(
        IEnumerable<(SignatureWorkRow Row, Role Role)> items,
        SignatureWorkWindow window
    ) =>
        items
            .OrderBy(item => Urgency(item.Row, item.Role, window))
            .ThenBy(item => item.Row.ExpiresAtUtc ?? DateTime.MaxValue)
            .ThenBy(item => item.Row.Id);

    public static WorkBatchResponse Batch(
        IReadOnlyList<Guid> userIds,
        IReadOnlyList<SignatureWorkRow> rows,
        SignatureWorkWindow window
    ) =>
        new(
            userIds
                .Select(userId =>
                {
                    var mine = rows.Select(row => (Row: row, Role: RoleOf(row, userId)))
                        .Where(item => item.Role != Role.None)
                        .ToList();
                    return new WorkBatchRow(
                        userId,
                        mine.Count(item => item.Role is Role.Open or Role.Waiting),
                        mine.Count(item => IsOverdue(item.Row, item.Role, window))
                    );
                })
                .ToList()
        );

    // Si le toca firmar como preparador cuenta como abierta, aunque también sea la autora.
    private static Role RoleOf(SignatureWorkRow row, Guid userId)
    {
        var pendingPreparer = row.PreparerUserId == userId && !row.PreparerSigned;
        return row.Status switch
        {
            SignatureRequestStatus.InProgress when pendingPreparer => Role.Open,
            SignatureRequestStatus.InProgress when row.CreatedByUserId == userId => Role.Waiting,
            SignatureRequestStatus.Expired when pendingPreparer || row.CreatedByUserId == userId => Role.Expired,
            _ => Role.None,
        };
    }

    private static bool IsOverdue(SignatureWorkRow row, Role role, SignatureWorkWindow window) =>
        role == Role.Expired || row.ExpiresAtUtc is { } expires && WorkDue.IsOverdue(expires, window.NowUtc);

    private static bool IsDueSoon(SignatureWorkRow row, Role role, SignatureWorkWindow window) =>
        role != Role.Expired
        && row.ExpiresAtUtc is { } expires
        && WorkDue.IsDueSoon(expires, window.NowUtc, window.DueSoonEndUtc);

    private static int Urgency(SignatureWorkRow row, Role role, SignatureWorkWindow window) =>
        IsOverdue(row, role, window) ? 0
        : IsDueSoon(row, role, window) ? 1
        : role == Role.Open ? 2
        : 3;

    private static WorkItem ToItem(SignatureWorkRow row, Role role) =>
        new(
            row.Id,
            WorkKinds.Signatures,
            row.Title,
            row.CustomerId,
            row.ExpiresAtUtc,
            DueDate: null,
            role switch
            {
                Role.Open => "awaiting-preparer",
                Role.Waiting => "awaiting-signers",
                _ => "expired",
            },
            $"/signature/requests/{row.Id}"
        );
}
