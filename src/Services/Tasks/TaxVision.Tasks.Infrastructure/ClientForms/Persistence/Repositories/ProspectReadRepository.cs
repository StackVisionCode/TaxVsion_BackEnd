using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Participants.Prospects;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

/// <summary>
/// Prospectos = FormParticipants del tenant. El estado se deriva en SQL (ver <see cref="ProspectStatus"/>)
/// para poder filtrar, contar y paginar en la base; el detalle de la página se carga después.
/// </summary>
internal sealed class ProspectReadRepository(TasksDbContext context) : IProspectReadRepository
{
    public async Task<ProspectSnapshotPage> ListAsync(ListProspectsQuery query, CancellationToken ct = default)
    {
        var rows = Filtered(query);
        var total = await rows.CountAsync(ct);
        var page = await Paged(rows, query).ToArrayAsync(ct);
        return new(await AssembleAsync(query.TenantId, query.ActorUserId, query.CanViewAll, page, ct), total);
    }

    // Filtro y página separados para que la prueba de traducción a SQL Server cubra la consulta real.
    internal IQueryable<ProspectRow> Filtered(ListProspectsQuery query)
    {
        var rows = Query(query.TenantId, query.ActorUserId, query.CanViewAll, query.Search);
        return query.Status is { } status ? rows.Where(item => item.Status == status) : rows;
    }

    internal static IQueryable<ProspectRow> Paged(IQueryable<ProspectRow> rows, ListProspectsQuery query) =>
        rows.OrderByDescending(item => item.LastActivityAtUtc)
            .ThenBy(item => item.Participant.Id)
            .Skip((query.Page - 1) * query.Size)
            .Take(query.Size);

    public async Task<ProspectSnapshot?> GetAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        Guid participantId,
        CancellationToken ct = default
    )
    {
        var row = await Query(tenantId, actorUserId, canViewAll, null)
            .Where(item => item.Participant.Id == participantId)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (await AssembleAsync(tenantId, actorUserId, canViewAll, [row], ct))[0];
    }

    // Consulta componible: ABAC, búsqueda y estado derivado.
    internal IQueryable<ProspectRow> Query(Guid tenantId, Guid actorUserId, bool canViewAll, string? search)
    {
        var requests = context.FormRequests.IgnoreQueryFilters().Where(item => item.TenantId == tenantId);
        var runs = context
            .FormActionRuns.IgnoreQueryFilters()
            .Where(run =>
                run.TenantId == tenantId
                && run.ActionType == FormActionNames.CreateCustomer
                && run.Status != FormActionRunStatus.Skipped
                && run.RetriedAtUtc == null
            );
        var participants = context
            .FormParticipants.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId);
        if (!canViewAll)
        {
            // Mismo criterio que la bandeja para externas: responsable o solicitante; vinculado, por asignación.
            participants = participants.Where(participant =>
                requests.Any(request =>
                    request.ParticipantId == participant.Id
                    && (request.ResponsibleUserId == actorUserId || request.RequestedByUserId == actorUserId)
                )
                || (
                    participant.LinkedCustomerId != null
                    && context
                        .CustomerAssignmentProjections.IgnoreQueryFilters()
                        .Any(assignment =>
                            assignment.TenantId == tenantId
                            && assignment.CustomerId == participant.LinkedCustomerId
                            && assignment.UserId == actorUserId
                        )
                )
            );
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            participants = participants.Where(participant =>
                EF.Functions.Like(participant.Email, pattern, "\\")
                || (participant.FullName != null && EF.Functions.Like(participant.FullName, pattern, "\\"))
            );
        }

        return participants
            .Select(participant => new
            {
                Participant = participant,
                HasSubmitted = requests.Any(request =>
                    request.ParticipantId == participant.Id && request.CurrentRevisionNumber > 0
                ),
                IsDuplicate = runs.Where(run =>
                        requests.Any(request =>
                            request.Id == run.FormRequestId && request.ParticipantId == participant.Id
                        )
                    )
                    .OrderByDescending(run => run.RequestedAtUtc)
                    .Select(run =>
                        run.Status == FormActionRunStatus.Rejected
                        && run.ErrorCode == FormActionRunErrors.DuplicateFoundCode
                    )
                    .FirstOrDefault(),
                DuplicateOf = runs.Where(run =>
                        requests.Any(request =>
                            request.Id == run.FormRequestId && request.ParticipantId == participant.Id
                        )
                    )
                    .OrderByDescending(run => run.RequestedAtUtc)
                    .Select(run => run.ExistingCustomerId)
                    .FirstOrDefault(),
                LastRequestActivity = requests
                    .Where(request => request.ParticipantId == participant.Id)
                    .Max(request => (DateTime?)request.LastActivityAtUtc),
            })
            .Select(item => new
            {
                item.Participant,
                item.IsDuplicate,
                item.DuplicateOf,
                item.HasSubmitted,
                Activity = item.LastRequestActivity ?? item.Participant.CreatedAtUtc,
            })
            .Select(item => new ProspectRow
            {
                Participant = item.Participant,
                Status =
                    item.Participant.LostAtUtc != null ? ProspectStatus.Lost
                    : item.Participant.LinkedCustomerId != null ? ProspectStatus.Converted
                    : item.IsDuplicate ? ProspectStatus.Duplicate
                    : item.HasSubmitted ? ProspectStatus.Submitted
                    : item.Participant.VerifiedAtUtc != null ? ProspectStatus.Verified
                    : ProspectStatus.Invited,
                DuplicateOf = item.IsDuplicate ? item.DuplicateOf : null,
                LastActivityAtUtc =
                    item.Participant.LostAtUtc != null && item.Participant.LostAtUtc > item.Activity
                        ? item.Participant.LostAtUtc.Value
                        : item.Activity,
            });
    }

    private async Task<IReadOnlyList<ProspectSnapshot>> AssembleAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        IReadOnlyList<ProspectRow> rows,
        CancellationToken ct
    )
    {
        var participantIds = rows.Select(item => item.Participant.Id).ToArray();
        var requests = await context
            .FormRequests.IgnoreQueryFilters()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.ActionRuns)
            .Where(item =>
                item.TenantId == tenantId
                && item.ParticipantId != null
                && participantIds.Contains(item.ParticipantId.Value)
            )
            .ToArrayAsync(ct);
        var versionIds = requests.Select(item => item.FormTemplateVersionId).Distinct().ToArray();
        var templates = await context
            .FormTemplateVersions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && versionIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Title, ct);
        var visibleDuplicates = await VisibleCustomersAsync(tenantId, actorUserId, canViewAll, rows, ct);
        var byParticipant = requests.ToLookup(item => item.ParticipantId!.Value);
        return rows.Select(row => new ProspectSnapshot(
                row.Participant,
                row.Status,
                row.LastActivityAtUtc,
                row.DuplicateOf is { } duplicate && visibleDuplicates.Contains(duplicate) ? duplicate : null,
                byParticipant[row.Participant.Id].ToArray(),
                templates
            ))
            .ToArray();
    }

    // El cliente duplicado solo se muestra con ABAC sobre él, igual que en la bandeja.
    private async Task<IReadOnlySet<Guid>> VisibleCustomersAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        IReadOnlyList<ProspectRow> rows,
        CancellationToken ct
    )
    {
        var ids = rows.Where(item => item.DuplicateOf.HasValue)
            .Select(item => item.DuplicateOf!.Value)
            .Distinct()
            .ToArray();
        if (canViewAll || ids.Length == 0)
            return ids.ToHashSet();
        return (
            await context
                .CustomerAssignmentProjections.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.UserId == actorUserId && ids.Contains(item.CustomerId))
                .Select(item => item.CustomerId)
                .ToArrayAsync(ct)
        ).ToHashSet();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    internal sealed class ProspectRow
    {
        public FormParticipant Participant { get; init; } = null!;
        public ProspectStatus Status { get; init; }
        public Guid? DuplicateOf { get; init; }
        public DateTime LastActivityAtUtc { get; init; }
    }
}
