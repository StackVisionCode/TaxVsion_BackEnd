using System.Text.Json;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Prospects;

// Quién actúa sobre qué prospecto, con lo que el actor puede ver y hacer.
internal sealed record ProspectScope(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    bool CanStartCustomerCreation,
    Guid ParticipantId
);

internal static class ProspectCommandSupport
{
    // Mismo ABAC que el listado: un prospecto que el actor no ve responde 404, no 403.
    internal static async Task<Result<FormParticipant>> LoadVisibleAsync(
        ProspectScope scope,
        IProspectReadRepository reads,
        IFormParticipantRepository participants,
        CancellationToken ct
    )
    {
        var visible = await reads.GetAsync(
            scope.TenantId,
            scope.ActorUserId,
            scope.CanViewAll,
            scope.ParticipantId,
            ct
        );
        var participant = visible is null ? null : await participants.GetAsync(scope.TenantId, scope.ParticipantId, ct);
        return participant is null
            ? Result.Failure<FormParticipant>(FormParticipantErrors.NotFound)
            : Result.Success(participant);
    }

    // Solo el id del prospecto: el motivo puede traer texto libre y no va al audit.
    internal static void Audit(ProspectScope scope, string action, IFormAuditLog audit) =>
        audit.Record(
            scope.TenantId,
            null,
            null,
            action,
            scope.ActorUserId,
            JsonSerializer.Serialize(new { participantId = scope.ParticipantId })
        );

    internal static async Task<Result<ProspectResponse>> ReadBackAsync(
        ProspectScope scope,
        IProspectReadRepository reads,
        ProspectResponseMapper mapper,
        CancellationToken ct
    )
    {
        var snapshot = await reads.GetAsync(
            scope.TenantId,
            scope.ActorUserId,
            scope.CanViewAll,
            scope.ParticipantId,
            ct
        );
        if (snapshot is null)
            return Result.Failure<ProspectResponse>(FormParticipantErrors.NotFound);
        var mapped = await mapper.MapAsync(
            [snapshot],
            scope.TenantId,
            scope.ActorUserId,
            scope.CanViewAll,
            scope.CanStartCustomerCreation,
            ct
        );
        return Result.Success(mapped[0]);
    }
}
