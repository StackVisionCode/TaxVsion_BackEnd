using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Prospects;

/// <summary>
/// Arma la fila del prospecto. CanCreateClient reusa las reglas del arranque manual del alta
/// (POST actions/create-customer): permisos del actor, ABAC de la request, request externa sin vincular,
/// con revisión enviada, sin un intento vigente que no admita reintento y con la acción en su versión.
/// Las requests canceladas y los prospectos ya convertidos no se ofrecen.
/// </summary>
public sealed class ProspectResponseMapper(IFormTemplateVersionRepository versions, IFormEngine engine)
{
    public async Task<IReadOnlyList<ProspectResponse>> MapAsync(
        IReadOnlyList<ProspectSnapshot> snapshots,
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        bool canStartCustomerCreation,
        CancellationToken ct
    )
    {
        var actionByVersion = new Dictionary<Guid, bool>();
        var items = new List<ProspectResponse>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            Guid? creatable = null;
            // Un prospecto ya convertido no ofrece crear otro cliente.
            if (canStartCustomerCreation && snapshot.Participant.LinkedCustomerId is null)
            {
                foreach (var request in snapshot.Requests.OrderByDescending(item => item.RequestedAtUtc))
                {
                    if (
                        CanStart(request, actorUserId, canViewAll)
                        && await HasCreateCustomerAsync(tenantId, request.FormTemplateVersionId, actionByVersion, ct)
                    )
                    {
                        creatable = request.Id;
                        break;
                    }
                }
            }
            items.Add(Map(snapshot, creatable));
        }
        return items;
    }

    private static ProspectResponse Map(ProspectSnapshot snapshot, Guid? creatable)
    {
        var participant = snapshot.Participant;
        var latest = snapshot.Requests.OrderByDescending(item => item.RequestedAtUtc).FirstOrDefault();
        return new(
            participant.Id,
            participant.Email,
            participant.FullName,
            participant.PhoneE164,
            snapshot.Status,
            participant.Source,
            participant.CreatedAtUtc,
            snapshot.LastActivityAtUtc,
            snapshot.Requests.Count,
            latest is null
                ? null
                : new ProspectLatestRequestResponse(
                    latest.Id,
                    latest.Title,
                    latest.Status,
                    snapshot.TemplateNames.GetValueOrDefault(latest.FormTemplateVersionId, "Form")
                ),
            participant.LinkedCustomerId,
            snapshot.Status == ProspectStatus.Duplicate ? snapshot.DuplicateOfCustomerId : null,
            participant.LostReason,
            creatable.HasValue,
            creatable
        );
    }

    private static bool CanStart(FormRequest request, Guid actorUserId, bool canViewAll)
    {
        if (
            request.Status == FormRequestStatus.Cancelled
            || request.ParticipantKind != FormParticipantKind.External
            || request.CustomerLinkStatus != CustomerLinkStatus.None
            || !request.ResponsibleUserId.HasValue
            || request.CurrentRevisionNumber < 1
        )
            return false;
        if (!canViewAll && request.ResponsibleUserId != actorUserId && request.RequestedByUserId != actorUserId)
            return false;
        var latest = request.LatestAction(FormActionNames.CreateCustomer);
        return latest is null || latest.IsRetryable;
    }

    private async Task<bool> HasCreateCustomerAsync(
        Guid tenantId,
        Guid versionId,
        Dictionary<Guid, bool> cache,
        CancellationToken ct
    )
    {
        if (cache.TryGetValue(versionId, out var known))
            return known;
        var version = await versions.GetByIdAsync(tenantId, versionId, ct);
        var rules = version.IsSuccess
            ? engine.ParseRuleSets(version.Value.MappingSetJson, version.Value.ActionSetJson)
            : null;
        var has =
            rules is { IsSuccess: true } && rules.Value.Actions.Any(item => item.Type == FormActionType.CreateCustomer);
        cache[versionId] = has;
        return has;
    }
}
