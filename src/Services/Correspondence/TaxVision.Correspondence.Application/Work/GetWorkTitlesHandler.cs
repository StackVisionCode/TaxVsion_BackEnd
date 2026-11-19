using BuildingBlocks.Contracts.Work;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Work;

/// <summary>
/// <paramref name="VisibleAccountIds"/> null = todos los buzones; <paramref name="RestrictToUserId"/> null = todos los
/// clientes.
/// </summary>
public sealed record GetWorkTitlesQuery(
    Guid TenantId,
    IReadOnlyList<WorkTitleRef> Items,
    IReadOnlyCollection<Guid>? VisibleAccountIds,
    Guid? RestrictToUserId
);

/// <summary>
/// Títulos en vivo del historial (<c>POST /correspondence/work/titles</c>): asunto de un correo enviado
/// (<c>email_message</c>, id del borrador) o de un hilo (<c>email_thread</c>). Omite lo que no se ve y los tipos ajenos.
/// </summary>
public static class GetWorkTitlesHandler
{
    public static async Task<WorkTitlesResponse> Handle(
        GetWorkTitlesQuery query,
        IMailTitleReader reader,
        CancellationToken ct
    )
    {
        var messageIds = IdsOf(query.Items, WorkResourceTypes.EmailMessage);
        var threadIds = IdsOf(query.Items, WorkResourceTypes.EmailThread);

        var messages =
            messageIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await reader.GetSentSubjectsAsync(
                    query.TenantId,
                    messageIds,
                    query.VisibleAccountIds,
                    query.RestrictToUserId,
                    ct
                );
        var threads =
            threadIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await reader.GetThreadSubjectsAsync(
                    query.TenantId,
                    threadIds,
                    query.VisibleAccountIds,
                    query.RestrictToUserId,
                    ct
                );

        var titles = new List<WorkTitle>();
        var seen = new HashSet<(string, Guid)>();
        foreach (var item in query.Items)
        {
            var source = item.ResourceType switch
            {
                WorkResourceTypes.EmailMessage => messages,
                WorkResourceTypes.EmailThread => threads,
                _ => null,
            };
            if (source is null || !seen.Add((item.ResourceType, item.Id)))
                continue;
            if (WorkLimits.ClipTitle(source.GetValueOrDefault(item.Id)) is { } title)
                titles.Add(new WorkTitle(item.ResourceType, item.Id, title));
        }
        return new WorkTitlesResponse(titles);
    }

    private static List<Guid> IdsOf(IReadOnlyList<WorkTitleRef> items, string type) =>
        items.Where(i => i.ResourceType == type).Select(i => i.Id).Distinct().ToList();
}
