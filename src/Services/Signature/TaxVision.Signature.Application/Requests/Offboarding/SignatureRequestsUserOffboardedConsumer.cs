using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Preparer;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Offboarding;

/// <summary>
/// Al RETIRAR a un empleado, sus solicitudes de firma (D-2 del retiro, 2026-11-03):
/// <list type="number">
///   <item>Sin enviar escritas por él → pasan al sucesor; sin sucesor, los borradores se descartan (los
///     programados, con legal hold o de origen externo se conservan y se avisa a los admins).</item>
///   <item>Borradores donde es preparador → se le quita (y su firma personal congelada) y se avisa al autor.</item>
///   <item>Enviadas donde es preparador sin firmar → el preparador pasa al sucesor si tiene identidad 8879
///     propia (auditado); si no se puede, se avisa a los admins — nunca queda bloqueada en silencio.</item>
/// </list>
/// Lo enviado nunca cambia de autor y lo firmado/sellado/auditado no se toca. Idempotente: lo ya resuelto
/// deja de coincidir con las consultas; sin orden asumido (el sucesor se valida contra la proyección local).
/// </summary>
public static class SignatureRequestsUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent evt,
        ISignatureRequestStaffWorkRepository staffWork,
        ISignatureRequestRepository requests,
        IAuthzUserPermissionsProjectionRepository permissions,
        ISignatureProfileRepository profiles,
        PreparerReassigner reassigner,
        PreparerPermissionCodes permissionCodes,
        IUnitOfWork unitOfWork,
        ISignatureRequestListCacheInvalidator listCache,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureRequest> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;
        using (correlation.Push(correlationId))
        {
            var context = new OffboardingContext(evt, correlationId, permissionCodes.AdminAudience);
            var successor = await ResolveAuthorSuccessorAsync(evt, permissions, ct);

            var removed = await HandleUnsentAsync(context, successor, staffWork, requests, ct);
            var withPreparer = await staffWork.ListOpenWithPreparerAsync(evt.TenantId, evt.UserId, ct);
            var drafts = withPreparer.Where(r => IsEditable(r) && !removed.Contains(r.Id)).ToList();
            await ClearDraftPreparersAsync(context, drafts, profiles, ct);

            var touched = context.Changes > 0;
            if (touched)
                await unitOfWork.SaveChangesAsync(ct);

            foreach (var request in withPreparer.Where(r => !IsEditable(r) && !removed.Contains(r.Id)))
            {
                if (await ReassignSentAsync(context, request, successor, reassigner, ct))
                {
                    await unitOfWork.SaveChangesAsync(ct);
                    touched = true;
                }
            }

            if (touched)
                await listCache.InvalidateAsync(evt.TenantId, ct);

            // Guardado antes de publicar: un reintento no deja avisos de cambios que no quedaron.
            foreach (var message in context.Outbox)
                await bus.PublishAsync(message);

            logger.LogInformation(
                "Offboarded user {UserId} in tenant {TenantId}: {Changes} signature request change(s), {Notices} notice(s).",
                evt.UserId,
                evt.TenantId,
                context.Changes,
                context.Outbox.Count
            );
        }
    }

    // Auth ya validó al sucesor (empleado activo del mismo tenant); localmente solo se descarta si la
    // proyección dice que NO está activo. Para el preparador, el reasignador exige además proyección activa.
    private static async Task<Guid?> ResolveAuthorSuccessorAsync(
        UserOffboardedIntegrationEvent evt,
        IAuthzUserPermissionsProjectionRepository permissions,
        CancellationToken ct
    )
    {
        if (evt.SuccessorUserId is not { } successor || successor == Guid.Empty || successor == evt.UserId)
            return null;

        var member = await permissions.GetAsync(evt.TenantId, successor, ct);
        return member is { IsActive: false } ? null : successor;
    }

    private static async Task<HashSet<Guid>> HandleUnsentAsync(
        OffboardingContext context,
        Guid? successor,
        ISignatureRequestStaffWorkRepository staffWork,
        ISignatureRequestRepository requests,
        CancellationToken ct
    )
    {
        var removed = new HashSet<Guid>();
        var unsent = await staffWork.ListUnsentAuthoredByAsync(context.Event.TenantId, context.Event.UserId, ct);
        foreach (var request in unsent)
        {
            if (successor is { } newAuthor)
            {
                if (request.ReassignAuthor(newAuthor).IsSuccess)
                    context.Changes++;
                continue;
            }

            if (CanDiscard(request))
            {
                requests.Remove(request);
                removed.Add(request.Id);
                context.Changes++;
                continue;
            }

            context.Notify(
                request,
                SignaturePreparerAttentionReasons.UnsentRequestKept,
                "Its author was offboarded without a successor. Assign it to someone or delete it.",
                recipient: null
            );
        }
        return removed;
    }

    // Borrador descartable: sin legal hold y creado por el staff (uno de otro servicio lo correlaciona su origen).
    private static bool CanDiscard(SignatureRequest request) =>
        request.EnsureCanBeDeleted().IsSuccess && !request.LegalHold && request.ExternalReference is null;

    private static async Task ClearDraftPreparersAsync(
        OffboardingContext context,
        IReadOnlyList<SignatureRequest> drafts,
        ISignatureProfileRepository profiles,
        CancellationToken ct
    )
    {
        foreach (var request in drafts)
        {
            if (request.ClearPreparer().IsFailure)
                continue;

            // Su firma personal congelada tampoco debe salir estampada; la de oficina se queda.
            if (request.PreparerSignatureFileId is { } fileId)
            {
                var profile = await profiles.GetByFileIdAsync(request.TenantId, fileId, ct);
                if (profile?.OwnerUserId == context.Event.UserId)
                    request.ClearPreparerSignature();
            }

            context.Changes++;
            var author = request.CreatedByUserId == context.Event.UserId ? (Guid?)null : request.CreatedByUserId;
            context.Notify(
                request,
                SignaturePreparerAttentionReasons.PreparerCleared,
                "Its preparer was offboarded. Choose a new preparer before sending it.",
                author
            );
        }
    }

    private static async Task<bool> ReassignSentAsync(
        OffboardingContext context,
        SignatureRequest request,
        Guid? successor,
        PreparerReassigner reassigner,
        CancellationToken ct
    )
    {
        if (successor is not { } newPreparer)
        {
            context.Notify(
                request,
                SignaturePreparerAttentionReasons.PreparerChangeBlocked,
                "Its preparer was offboarded without a successor. Change the preparer so the return can be signed.",
                recipient: null
            );
            return false;
        }

        var result = await reassigner.ReassignAsync(
            request,
            newPreparer,
            changedByUserId: null,
            SignaturePreparerChangeReasons.Offboarding,
            ct
        );
        if (result.IsFailure)
        {
            context.Notify(
                request,
                SignaturePreparerAttentionReasons.PreparerChangeBlocked,
                $"Its preparer was offboarded and the successor could not take over: {result.Error.Message}",
                recipient: null
            );
            return false;
        }

        if (!result.Value.Changed)
            return false;

        context.Changes++;
        context.Outbox.Add(
            new SignaturePreparerChangedIntegrationEvent
            {
                TenantId = request.TenantId,
                CorrelationId = context.CorrelationId,
                SignatureRequestId = request.Id,
                Title = request.Title,
                CreatedByUserId = request.CreatedByUserId,
                PreviousPreparerUserId = result.Value.PreviousPreparerUserId,
                NewPreparerUserId = newPreparer,
                NewPreparerDisplayName = result.Value.NewPreparer.DisplayName,
                ChangedByUserId = null,
                Reason = SignaturePreparerChangeReasons.Offboarding,
                ChangedAtUtc = request.UpdatedAtUtc,
            }
        );
        return true;
    }

    private static bool IsEditable(SignatureRequest request) => request.EnsureCanBeDeleted().IsSuccess;

    private sealed class OffboardingContext(
        UserOffboardedIntegrationEvent evt,
        string correlationId,
        string adminAudience
    )
    {
        public UserOffboardedIntegrationEvent Event { get; } = evt;
        public string CorrelationId { get; } = correlationId;
        public int Changes { get; set; }
        public List<object> Outbox { get; } = [];

        public void Notify(SignatureRequest request, string reason, string detail, Guid? recipient) =>
            Outbox.Add(
                new SignaturePreparerAttentionRequiredIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = CorrelationId,
                    SignatureRequestId = request.Id,
                    Title = request.Title,
                    Status = request.Status.ToString(),
                    Reason = reason,
                    Detail = detail,
                    OffboardedUserId = Event.UserId,
                    CreatedByUserId = request.CreatedByUserId,
                    RecipientUserId = recipient,
                    AudiencePermission = adminAudience,
                }
            );
    }
}
