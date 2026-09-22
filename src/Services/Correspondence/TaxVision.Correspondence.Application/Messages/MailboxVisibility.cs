using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Gate de buzón de oficina a nivel de UN mensaje (body/metadata/adjuntos): con el set visible en
/// null el usuario ve todo; si no, el mensaje solo es accesible si su hilo tiene ≥1 mensaje de un
/// buzón visible (coherente con el "hilo entero" de los listados — no se bloquea mensaje a mensaje
/// dentro de un hilo ya visible).
/// </summary>
internal static class MailboxVisibility
{
    public static async Task<bool> CanSeeMessageAsync(
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid tenantId,
        IncomingEmail email,
        IEmailThreadRepository emailThreads,
        CancellationToken ct
    )
    {
        if (visibleAccountIds is null)
            return true;

        return await emailThreads.HasVisibleMessageAsync(tenantId, email.EmailThreadId, visibleAccountIds, ct);
    }
}
