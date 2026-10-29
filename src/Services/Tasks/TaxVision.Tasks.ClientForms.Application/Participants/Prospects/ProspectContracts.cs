using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Prospects;

/// <summary>
/// Estado de un prospecto (un FormParticipant del tenant), derivado en este orden; gana la primera regla:
/// 1. Lost: marcado como perdido por el staff.
/// 2. Converted: vinculado a un cliente.
/// 3. Duplicate: el último alta de cliente (CreateCustomer) de sus requests se rechazó por DuplicateFound.
/// 4. Submitted: alguna de sus requests tiene una revisión enviada.
/// 5. Verified: verificó su email con el código.
/// 6. Invited: ninguna de las anteriores.
/// </summary>
public enum ProspectStatus
{
    Invited = 1,
    Verified = 2,
    Submitted = 3,
    Converted = 4,
    Duplicate = 5,
    Lost = 6,
}

public sealed record ListProspectsQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    bool CanStartCustomerCreation,
    ProspectStatus? Status,
    string? Search,
    int Page,
    int Size
);

public sealed record MarkProspectLostCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    bool CanStartCustomerCreation,
    Guid ParticipantId,
    string? Reason,
    DateTime NowUtc
);

public sealed record ReopenProspectCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    bool CanStartCustomerCreation,
    Guid ParticipantId,
    DateTime NowUtc
);

public sealed record ProspectLatestRequestResponse(
    Guid Id,
    string Title,
    FormRequestStatus Status,
    string TemplateName
);

public sealed record ProspectResponse(
    Guid ParticipantId,
    string Email,
    string? FullName,
    string? PhoneE164,
    ProspectStatus Status,
    FormParticipantSource Source,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc,
    int RequestCount,
    ProspectLatestRequestResponse? LatestRequest,
    Guid? CustomerId,
    Guid? DuplicateOfCustomerId,
    string? LostReason,
    bool CanCreateClient,
    // Aditivo: la request sobre la que llamar POST tasks/forms/requests/{id}/actions/create-customer.
    Guid? CreateClientRequestId
);

public sealed record ProspectPageResponse(IReadOnlyList<ProspectResponse> Items, int Page, int Size, int TotalCount);

// Lo que lee el repositorio: participante, estado ya derivado y sus requests (con runs, sin rastrear).
// DuplicateOfCustomerId llega filtrado por ABAC del cliente, igual que en la bandeja.
public sealed record ProspectSnapshot(
    FormParticipant Participant,
    ProspectStatus Status,
    DateTime LastActivityAtUtc,
    Guid? DuplicateOfCustomerId,
    IReadOnlyList<FormRequest> Requests,
    IReadOnlyDictionary<Guid, string> TemplateNames
);

public sealed record ProspectSnapshotPage(IReadOnlyList<ProspectSnapshot> Items, int TotalCount);

public interface IProspectReadRepository
{
    Task<ProspectSnapshotPage> ListAsync(ListProspectsQuery query, CancellationToken ct = default);

    // Null si no existe o el actor no lo ve (misma regla ABAC que el listado).
    Task<ProspectSnapshot?> GetAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        Guid participantId,
        CancellationToken ct = default
    );
}
