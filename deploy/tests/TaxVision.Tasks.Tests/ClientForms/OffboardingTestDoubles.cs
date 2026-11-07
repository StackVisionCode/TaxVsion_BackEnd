using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.Tests.ClientForms;

internal static class OffboardingFixtures
{
    internal static UserOffboardedIntegrationEvent Offboarded(
        Guid tenantId,
        Guid leaver,
        Guid? successor,
        Guid? offboardedBy = null
    ) =>
        new()
        {
            TenantId = tenantId,
            UserId = leaver,
            Email = "leaver@example.com",
            ActorType = "TenantEmployee",
            SuccessorUserId = successor,
            OffboardedByUserId = offboardedBy,
            RemovedAtUtc = DateTime.UtcNow,
        };

    internal static FormIntakeLink Link(Guid tenantId, Guid versionId, Guid responsible) =>
        FormIntakeLink
            .Create(
                tenantId,
                "Public intake",
                Guid.NewGuid(),
                versionId,
                Guid.NewGuid(),
                1,
                $"hash-{Guid.NewGuid():N}",
                "oken",
                "cipher",
                1,
                null,
                null,
                responsible,
                responsible,
                DateTime.UtcNow
            )
            .Value;
}

// Activos = los listados; con permisos = activos y no excluidos.
internal sealed class OffboardingResponsibleUsers(
    IEnumerable<Guid> active,
    IEnumerable<Guid>? withoutPermissions = null
) : IFormResponsibleUserValidator
{
    private readonly HashSet<Guid> _active = [.. active];
    private readonly HashSet<Guid> _withoutPermissions = [.. withoutPermissions ?? []];

    public Task<bool> IsActiveAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_active.Contains(userId));

    public Task<bool> HasPermissionsAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<string> permissions,
        CancellationToken ct = default
    ) => Task.FromResult(_active.Contains(userId) && !_withoutPermissions.Contains(userId));
}

internal sealed class OffboardingAuthorizer(IEnumerable<Guid>? denied = null) : IFormActionAuthorizer
{
    private readonly HashSet<Guid> _denied = [.. denied ?? []];

    public Task<Result> EnsureResponsibleCanRunAsync(
        Guid tenantId,
        Guid responsibleUserId,
        FormTemplateVersion version,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _denied.Contains(responsibleUserId)
                ? Result.Failure(FormRequestErrors.ResponsibleNotAuthorized)
                : Result.Success()
        );
}

internal sealed class OffboardingIntakeLinks(params FormIntakeLink[] seed) : IFormIntakeLinkRepository
{
    public Task<bool> TryConsumeDailyIntakeAsync(
        Guid tenantId,
        DateTime nowUtc,
        int maxPerDay,
        CancellationToken ct = default
    ) => Task.FromResult(true);

    internal List<FormIntakeLink> Items { get; } = [.. seed];

    public Task<FormIntakeLink?> GetAsync(Guid tenantId, Guid linkId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(item => item.TenantId == tenantId && item.Id == linkId));

    public Task<FormIntakeLink?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(item => item.TokenHash == tokenHash));

    public Task<IReadOnlyList<FormIntakeLink>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FormIntakeLink>>([.. Items.Where(item => item.TenantId == tenantId)]);

    public Task<IReadOnlyList<FormIntakeLink>> ListNotRevokedByResponsibleAsync(
        Guid tenantId,
        Guid responsibleUserId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<FormIntakeLink>>([.. NotRevoked(tenantId, responsibleUserId)]);

    public Task<int> CountNotRevokedByResponsibleAsync(
        Guid tenantId,
        Guid responsibleUserId,
        CancellationToken ct = default
    ) => Task.FromResult(NotRevoked(tenantId, responsibleUserId).Count());

    public void Add(FormIntakeLink link) => Items.Add(link);

    private IEnumerable<FormIntakeLink> NotRevoked(Guid tenantId, Guid responsibleUserId) =>
        Items.Where(item =>
            item.TenantId == tenantId
            && item.ResponsibleUserId == responsibleUserId
            && item.Status != FormIntakeLinkStatus.Revoked
        );
}

internal sealed class OffboardingFormRequests(params FormRequest[] seed) : IFormRequestRepository
{
    internal List<FormRequest> Items { get; } = [.. seed];

    public Task<IReadOnlyList<FormRequest>> ListOpenByEffectiveResponsibleAsync(
        Guid tenantId,
        Guid userId,
        int take,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<FormRequest>>([.. OpenFor(tenantId, userId).Take(take)]);

    public Task<int> CountOpenByEffectiveResponsibleAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(OpenFor(tenantId, userId).Count());

    private IEnumerable<FormRequest> OpenFor(Guid tenantId, Guid userId) =>
        Items.Where(item =>
            item.TenantId == tenantId
            && item.Status is not (FormRequestStatus.Completed or FormRequestStatus.Cancelled)
            && (item.ResponsibleUserId ?? item.RequestedByUserId) == userId
        );

    public void Add(FormRequest request) => Items.Add(request);

    public Task<Result<FormRequest>> GetForCommandAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<Result<FormRequest>> GetForReviewAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<bool> HasOpenRequestAsync(
        Guid tenantId,
        Guid customerId,
        Guid templateId,
        Guid? exceptRequestId = null,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<bool> HasOpenExternalRequestAsync(
        Guid tenantId,
        Guid participantId,
        Guid templateId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<FormRequest>> ListOpenByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<FormRequest>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        bool onlyOpen,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<FormRequest>> ListByAttachmentFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<int> CountIntakeSubmissionsAsync(Guid tenantId, DateTime sinceUtc, CancellationToken ct = default) =>
        Task.FromResult(0);

    public Task<Guid?> FindOpenRequestIdAsync(
        Guid tenantId,
        Guid customerId,
        Guid templateId,
        Guid exceptRequestId,
        CancellationToken ct = default
    ) => Task.FromResult<Guid?>(null);

    public Task<FormRequest?> GetByPdfGenerationIdAsync(Guid generationId, CancellationToken ct = default) =>
        Task.FromResult<FormRequest?>(null);

    public Task<FormRequest?> GetForReminderAsync(Guid requestId, CancellationToken ct = default) =>
        Task.FromResult<FormRequest?>(null);

    public Task<FormRequest?> GetLatestIntakeRequestAsync(
        Guid tenantId,
        Guid intakeLinkId,
        IReadOnlyCollection<Guid> participantIds,
        CancellationToken ct = default
    ) => Task.FromResult<FormRequest?>(null);

    public Task<FormRequest?> GetOpenExternalRequestAsync(
        Guid tenantId,
        Guid participantId,
        Guid templateId,
        CancellationToken ct = default
    ) => Task.FromResult<FormRequest?>(null);

    public Task<IReadOnlyList<FormRequest>> ListOpenByParticipantAsync(
        Guid tenantId,
        Guid participantId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);

    public Task<IReadOnlyList<FormRequest>> ListReminderCandidatesAsync(
        DateTime dueThresholdUtc,
        int maxReminders,
        int take,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);
}
