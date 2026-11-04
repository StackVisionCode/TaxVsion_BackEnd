using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reglas de visibilidad del correo compartidas por los listados, la búsqueda y el conteo: gate de
/// buzón (<c>null</c> = todos los buzones) y cliente asignado (<c>null</c> = todos los clientes). Una sola
/// fuente para que el Inbox, Sent/Drafts/Trash y <c>/search</c> no puedan divergir.
/// </summary>
internal static class MailVisibilityQueries
{
    /// <summary>Hilos del tenant que el usuario puede ver: gate de buzón + (si no ve todos) cliente asignado.</summary>
    public static IQueryable<EmailThread> VisibleThreads(
        CorrespondenceDbContext db,
        Guid tenantId,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId
    )
    {
        var query = WithMailboxGate(
            db,
            db.EmailThreads.AsNoTracking().IgnoreQueryFilters().Where(x => x.TenantId == tenantId),
            visibleAccountIds
        );
        // Un hilo sin cliente (D-1) lo decide solo el buzón (D-2): no pasa por las asignaciones.
        if (restrictToUserId is { } userId)
            query = query.Where(x =>
                x.CustomerId == null
                || db.Set<CustomerAssignmentProjection>()
                    .Any(a => a.TenantId == tenantId && a.CustomerId == x.CustomerId && a.UserId == userId)
            );
        return query;
    }

    public static IQueryable<EmailThread> WithMailboxGate(
        CorrespondenceDbContext db,
        IQueryable<EmailThread> query,
        IReadOnlyCollection<Guid>? visibleAccountIds
    )
    {
        if (visibleAccountIds is null)
        {
            // Sin gate: se oculta solo el hilo SIN contenido visible (0 entrantes vivos y 0 enviados vivos).
            return query.Where(x =>
                x.MessageCount > 0
                || db.Drafts.Any(d => d.EmailThreadId == x.Id && d.Status == DraftStatus.Sent && d.DeletedAtUtc == null)
            );
        }

        // Gate de buzón de oficina: el hilo debe tener ≥1 mensaje (entrante vivo o enviado) de un
        // buzón visible. Los hilos solo-oficina desaparecen; un hilo mixto se muestra entero.
        var ids = AsArray(visibleAccountIds);
        return query.Where(x =>
            db.IncomingEmails.Any(ie =>
                ie.EmailThreadId == x.Id && ie.DeletedAtUtc == null && ids.Contains(ie.AccountId)
            )
            || db.Drafts.Any(d =>
                d.EmailThreadId == x.Id
                && d.Status == DraftStatus.Sent
                && d.DeletedAtUtc == null
                && ids.Contains(d.AccountId)
            )
        );
    }

    /// <summary>
    /// Correo saliente del alcance pedido. IS NULL explícito y no "CustomerId == @p" con un parámetro
    /// nullable: EF traduciría esa comparación con la semántica de null de C# (un OR) y SQL Server dejaría
    /// de buscar por IX_Drafts_TenantId_CustomerId_Status_UpdatedAtUtc.
    /// </summary>
    public static IQueryable<Draft> OutgoingInScope(
        CorrespondenceDbContext db,
        IQueryable<Draft> drafts,
        DraftCustomerScope scope
    )
    {
        var assignments = db.Set<CustomerAssignmentProjection>();
        if (scope.IsAll)
        {
            // Sin cliente siempre; de cliente, solo los visibles para quien no tiene customers.view_all.
            return scope.RestrictToUserId is { } user
                ? drafts.Where(d =>
                    d.CustomerId == null
                    || assignments.Any(a =>
                        a.TenantId == d.TenantId && a.CustomerId == d.CustomerId && a.UserId == user
                    )
                )
                : drafts;
        }
        if (scope.CustomerId is { } customerId)
        {
            var ofCustomer = drafts.Where(d => d.CustomerId == customerId);
            return scope.RestrictToUserId is { } user
                ? ofCustomer.Where(d =>
                    assignments.Any(a => a.TenantId == d.TenantId && a.CustomerId == customerId && a.UserId == user)
                )
                : ofCustomer;
        }
        return drafts.Where(d => d.CustomerId == null);
    }

    /// <summary>Gate de buzón sobre el correo saliente: oculta lo enviado desde buzones no visibles.</summary>
    public static IQueryable<Draft> FromVisibleMailbox(
        IQueryable<Draft> drafts,
        IReadOnlyCollection<Guid>? visibleAccountIds
    )
    {
        if (visibleAccountIds is null)
            return drafts;
        var ids = AsArray(visibleAccountIds);
        return drafts.Where(d => ids.Contains(d.AccountId));
    }

    /// <summary>Entrantes de clientes visibles (cliente asignado) desde buzones visibles — la papelera los lista así.</summary>
    public static IQueryable<IncomingEmail> IncomingOfVisibleCustomers(
        CorrespondenceDbContext db,
        IQueryable<IncomingEmail> emails,
        Guid tenantId,
        Guid? restrictToUserId,
        IReadOnlyCollection<Guid>? visibleAccountIds
    )
    {
        if (restrictToUserId is { } user)
            emails = emails.Where(x =>
                db.Set<CustomerAssignmentProjection>()
                    .Any(a => a.TenantId == tenantId && a.CustomerId == x.CustomerId && a.UserId == user)
            );
        if (visibleAccountIds is not null)
        {
            var ids = AsArray(visibleAccountIds);
            emails = emails.Where(x => ids.Contains(x.AccountId));
        }
        return emails;
    }

    /// <summary>Saliente cuyo asunto o algún destinatario (dirección o nombre) contiene <paramref name="term"/>.</summary>
    public static IQueryable<Draft> MatchingOutgoing(IQueryable<Draft> drafts, string term) =>
        drafts.Where(d =>
            d.Subject.Contains(term)
            || d.Recipients.Any(r =>
                r.Address.Contains(term) || (r.DisplayName != null && r.DisplayName.Contains(term))
            )
        );

    /// <summary>Entrante cuyo asunto, remitente (dirección o nombre) o vista previa contiene <paramref name="term"/>.</summary>
    public static IQueryable<IncomingEmail> MatchingIncoming(IQueryable<IncomingEmail> emails, string term) =>
        emails.Where(x =>
            x.Subject.Contains(term)
            || x.From.Contains(term)
            || (x.FromDisplayName != null && x.FromDisplayName.Contains(term))
            || x.Snippet.Contains(term)
        );

    /// <summary>Saliente filtrado por texto, adjuntos y rango (fecha = <c>UpdatedAtUtc</c>: envío o último guardado).</summary>
    public static IQueryable<Draft> FilterOutgoing(IQueryable<Draft> drafts, MailListFilter? filter)
    {
        if (filter is null)
            return drafts;
        if (!string.IsNullOrWhiteSpace(filter.Search))
            drafts = MatchingOutgoing(drafts, filter.Search.Trim());
        if (filter.HasAttachmentsOnly)
            drafts = drafts.Where(d => d.Attachments.Any());
        if (filter.FromUtc is { } from)
            drafts = drafts.Where(d => d.UpdatedAtUtc >= from);
        if (filter.ToUtc is { } to)
            drafts = drafts.Where(d => d.UpdatedAtUtc <= to);
        return drafts;
    }

    /// <summary>Entrante filtrado por texto, adjuntos y rango (fecha = recepción).</summary>
    public static IQueryable<IncomingEmail> FilterIncoming(IQueryable<IncomingEmail> emails, MailListFilter? filter)
    {
        if (filter is null)
            return emails;
        if (!string.IsNullOrWhiteSpace(filter.Search))
            emails = MatchingIncoming(emails, filter.Search.Trim());
        if (filter.HasAttachmentsOnly)
            emails = emails.Where(x => x.HasAttachments);
        if (filter.FromUtc is { } from)
            emails = emails.Where(x => x.ReceivedAtUtc >= from);
        if (filter.ToUtc is { } to)
            emails = emails.Where(x => x.ReceivedAtUtc <= to);
        return emails;
    }

    /// <summary>Hilos con algún adjunto: de un entrante vivo o de un enviado vivo del hilo.</summary>
    public static IQueryable<EmailThread> ThreadsWithAttachments(
        CorrespondenceDbContext db,
        IQueryable<EmailThread> threads
    ) =>
        threads.Where(t =>
            db.IncomingEmails.Any(ie => ie.EmailThreadId == t.Id && ie.DeletedAtUtc == null && ie.HasAttachments)
            || db.Drafts.Any(d =>
                d.EmailThreadId == t.Id && d.Status == DraftStatus.Sent && d.DeletedAtUtc == null && d.Attachments.Any()
            )
        );

    /// <summary>Hilos con algo sin leer: un entrante vivo o un aviso de no entrega.</summary>
    public static IQueryable<EmailThread> ThreadsWithUnread(
        CorrespondenceDbContext db,
        IQueryable<EmailThread> threads,
        Guid tenantId
    )
    {
        var unreadNotices = UnreadNotices(db, tenantId);
        return threads.Where(t =>
            db.IncomingEmails.Any(ie => ie.EmailThreadId == t.Id && !ie.IsRead && ie.DeletedAtUtc == null)
            || unreadNotices.Any(n => n.EmailThreadId == t.Id)
        );
    }

    /// <summary>Hilos cuya última actividad cae en el rango (inclusivo; null = abierto).</summary>
    public static IQueryable<EmailThread> ThreadsInRange(
        IQueryable<EmailThread> threads,
        DateTime? fromUtc,
        DateTime? toUtc
    )
    {
        if (fromUtc is { } from)
            threads = threads.Where(t => t.LastMessageAtUtc >= from);
        if (toUtc is { } to)
            threads = threads.Where(t => t.LastMessageAtUtc <= to);
        return threads;
    }

    /// <summary>Avisos de no entrega sin leer (los Delayed nacen leídos y no cuentan, D-4).</summary>
    public static IQueryable<Domain.Delivery.DeliveryNotice> UnreadNotices(CorrespondenceDbContext db, Guid tenantId) =>
        db.DeliveryNotices.AsNoTracking().IgnoreQueryFilters().Where(n => n.TenantId == tenantId && !n.IsRead);

    public static Guid[] AsArray(IReadOnlyCollection<Guid> ids) => ids as Guid[] ?? ids.ToArray();
}
