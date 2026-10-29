using System.Reflection;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.Tests.ClientForms;

// Dobles compartidos de las pruebas de participantes externos, prospectos y enlaces de captación.

internal sealed class ExtParticipants(params FormParticipant[] seed) : IFormParticipantRepository
{
    internal List<FormParticipant> Items { get; } = [.. seed];

    public Task<FormParticipant?> GetAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
        Task.FromResult(Items.SingleOrDefault(item => item.TenantId == tenantId && item.Id == participantId));

    public Task<FormParticipant?> GetUnlinkedByEmailAsync(
        Guid tenantId,
        string email,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Items.SingleOrDefault(item =>
                item.TenantId == tenantId
                && item.Email == email.Trim().ToLowerInvariant()
                && item.LinkedCustomerId is null
            )
        );

    public Task<IReadOnlyList<Guid>> ListIdsByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(
            Items
                .Where(item => item.TenantId == tenantId && item.Email == email.Trim().ToLowerInvariant())
                .Select(item => item.Id)
                .ToArray()
        );

    public void Add(FormParticipant participant) => Items.Add(participant);

    public void Untrack(FormParticipant participant) { }
}

internal sealed class ExtRequests(params FormRequest[] seed) : IFormRequestRepository
{
    internal List<FormRequest> Items { get; } = [.. seed];

    private static bool IsOpen(FormRequest item) =>
        item.Status is not (FormRequestStatus.Completed or FormRequestStatus.Cancelled);

    public void Add(FormRequest request) => Items.Add(request);

    public Task<Result<FormRequest>> GetForCommandAsync(Guid tenantId, Guid requestId, CancellationToken ct = default)
    {
        var item = Items.SingleOrDefault(row => row.TenantId == tenantId && row.Id == requestId);
        return Task.FromResult(
            item is null ? Result.Failure<FormRequest>(FormRequestErrors.NotFound) : Result.Success(item)
        );
    }

    public Task<Result<FormRequest>> GetForReviewAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
        GetForCommandAsync(tenantId, requestId, ct);

    public Task<bool> HasOpenRequestAsync(
        Guid tenantId,
        Guid customerId,
        Guid templateId,
        Guid? exceptRequestId = null,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Items.Any(item =>
                item.TenantId == tenantId
                && item.CustomerId == customerId
                && item.FormTemplateId == templateId
                && IsOpen(item)
                && item.Id != exceptRequestId
            )
        );

    public Task<bool> HasOpenExternalRequestAsync(
        Guid tenantId,
        Guid participantId,
        Guid templateId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Items.Any(item =>
                item.TenantId == tenantId
                && item.ParticipantId == participantId
                && item.FormTemplateId == templateId
                && IsOpen(item)
            )
        );

    public Task<FormRequest?> GetOpenExternalRequestAsync(
        Guid tenantId,
        Guid participantId,
        Guid templateId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Items.FirstOrDefault(item =>
                item.TenantId == tenantId
                && item.ParticipantId == participantId
                && item.FormTemplateId == templateId
                && IsOpen(item)
            )
        );

    public Task<Guid?> FindOpenRequestIdAsync(
        Guid tenantId,
        Guid customerId,
        Guid templateId,
        Guid exceptRequestId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Items
                .Where(item =>
                    item.TenantId == tenantId
                    && item.CustomerId == customerId
                    && item.FormTemplateId == templateId
                    && item.Id != exceptRequestId
                    && IsOpen(item)
                )
                .Select(item => (Guid?)item.Id)
                .FirstOrDefault()
        );

    public Task<IReadOnlyList<FormRequest>> ListOpenByParticipantAsync(
        Guid tenantId,
        Guid participantId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<FormRequest>>(
            Items
                .Where(item => item.TenantId == tenantId && item.ParticipantId == participantId && IsOpen(item))
                .ToArray()
        );

    public Task<FormRequest?> GetLatestIntakeRequestAsync(
        Guid tenantId,
        Guid intakeLinkId,
        IReadOnlyCollection<Guid> participantIds,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Items
                .Where(item =>
                    item.TenantId == tenantId
                    && item.IntakeLinkId == intakeLinkId
                    && item.ParticipantId is { } participantId
                    && participantIds.Contains(participantId)
                    && item.Status != FormRequestStatus.Cancelled
                )
                .OrderByDescending(item => item.RequestedAtUtc)
                .FirstOrDefault()
        );

    public Task<IReadOnlyList<FormRequest>> ListOpenByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);

    public Task<IReadOnlyList<FormRequest>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        bool onlyOpen,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);

    public Task<IReadOnlyList<FormRequest>> ListByAttachmentFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FormRequest>>([]);
}

internal sealed class ExtGrants(params FormAccessGrant[] seed) : IFormAccessGrantRepository
{
    internal List<FormAccessGrant> Items { get; } = [.. seed];

    public Task<FormAccessGrant?> GetByRequestAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
        Task.FromResult(Items.LastOrDefault(item => item.TenantId == tenantId && item.FormRequestId == requestId));

    public Task<FormAccessGrant?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(Items.SingleOrDefault(item => item.TokenHash == tokenHash));

    public void Add(FormAccessGrant grant) => Items.Add(grant);
}

internal sealed class ExtChallenges : IFormAccessChallengeRepository
{
    internal List<FormAccessChallenge> Items { get; } = [];

    public Task<FormAccessChallenge?> GetAsync(Guid tenantId, Guid challengeId, CancellationToken ct = default) =>
        Task.FromResult(Items.SingleOrDefault(item => item.TenantId == tenantId && item.Id == challengeId));

    public Task<FormAccessChallenge?> GetLatestAsync(
        Guid tenantId,
        Guid? grantId,
        Guid? intakeLinkId,
        IReadOnlyCollection<string> emailLookupHashes,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Items
                .Where(item =>
                    item.TenantId == tenantId
                    && item.GrantId == grantId
                    && item.IntakeLinkId == intakeLinkId
                    && emailLookupHashes.Contains(item.EmailLookupHash)
                )
                .OrderByDescending(item => item.IssuedAtUtc)
                .FirstOrDefault()
        );

    public Task<int> CountIssuedAsync(
        Guid tenantId,
        IReadOnlyCollection<string> emailLookupHashes,
        DateTime sinceUtc,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Items.Count(item =>
                item.TenantId == tenantId
                && emailLookupHashes.Contains(item.EmailLookupHash)
                && item.IssuedAtUtc >= sinceUtc
            )
        );

    public Task<int> CountTenantIssuedAsync(Guid tenantId, DateTime sinceUtc, CancellationToken ct = default) =>
        Task.FromResult(Items.Count(item => item.TenantId == tenantId && item.IssuedAtUtc >= sinceUtc));

    public Task<bool> HasActiveBlockAsync(
        Guid tenantId,
        Guid? grantId,
        Guid? intakeLinkId,
        IReadOnlyCollection<string> emailLookupHashes,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Items.Any(item =>
                item.TenantId == tenantId
                && item.GrantId == grantId
                && item.IntakeLinkId == intakeLinkId
                && emailLookupHashes.Contains(item.EmailLookupHash)
                && item.IsBlockedAt(nowUtc)
            )
        );

    public void Add(FormAccessChallenge challenge) => Items.Add(challenge);
}

internal sealed class ExtSessions : IFormAccessSessionRepository
{
    internal List<FormAccessSession> Items { get; } = [];

    public Task<FormAccessSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(Items.LastOrDefault(item => item.SessionTokenHash == tokenHash));

    public Task<IReadOnlyList<FormAccessSession>> ListNotRevokedByRequestAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<FormAccessSession>>(
            Items.Where(item => item.TenantId == tenantId && item.FormRequestId == requestId).ToArray()
        );

    public void Add(FormAccessSession session) => Items.Add(session);
}

// Tokens fijos: "access-token" (dirigido) e "intake-token"/"intake-token-2" (captación); código 123456.
internal sealed class ExtSecurity : IFormExternalAccessSecurity
{
    public FormOpaqueToken GenerateToken() => new("session-token", "session-hash", "oken");

    public FormProtectedToken GenerateProtectedToken() => throw new NotSupportedException();

    public string HashToken(string rawToken) => rawToken.Replace("-token", "-hash", StringComparison.Ordinal);

    public string UnprotectToken(string tokenCipher, short keyVersion) => throw new NotSupportedException();

    public string GenerateOtp() => "123456";

    public string HashOtp(Guid tenantId, Guid sourceId, string email, string code) => "otp-hash";

    public bool VerifyOtp(Guid tenantId, Guid sourceId, string email, string code, string storedHash) =>
        code == "123456" && storedHash == "otp-hash";

    public FormProtectedEmail ProtectEmail(Guid tenantId, string email) => new($"cipher:{email}", 1, $"lookup:{email}");

    public string UnprotectEmail(string emailCipher, short keyVersion) => emailCipher["cipher:".Length..];

    public FormProtectedEmail ReEncryptEmail(Guid tenantId, string emailCipher, short fromKeyVersion) =>
        ProtectEmail(tenantId, UnprotectEmail(emailCipher, fromKeyVersion));

    public FormReEncryptedToken ReEncryptToken(string tokenCipher, short fromKeyVersion) =>
        new(tokenCipher, fromKeyVersion);

    public IReadOnlyCollection<string> EmailLookupHashes(Guid tenantId, string email) => [$"lookup:{email}"];
}

internal sealed class ExtHost(bool matches = true) : IFormPublicTenantHostValidator
{
    public Task<bool> MatchesTenantAsync(Guid tenantId, string host, CancellationToken ct = default) =>
        Task.FromResult(matches);
}

internal sealed class ExtCapabilities(bool external = true, bool intake = true) : IClientFormsPublicCapabilityEvaluator
{
    public Task<bool> IsExternalParticipantAvailableAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(external);

    public Task<bool> IsIntakeLinkAvailableAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(intake);
}

internal sealed class ExtCodec : IProtectedPayloadCodec
{
    public ProtectedPayload Protect<T>(string schema, ProtectedPayloadBinding binding, T content) =>
        new()
        {
            Cipher = "ciphertext",
            KeyVersion = 1,
            Schema = schema,
        };

    public T Unprotect<T>(ProtectedPayload payload, string expectedSchema, ProtectedPayloadBinding expectedBinding) =>
        throw new NotSupportedException();
}

// Guarda cada entrada con su resultado, incluidas las del sistema (avisos de vínculo).
internal sealed class ExtAuditLog : IFormAuditLog
{
    internal List<(string Action, string Outcome, Guid? RequestId, string? Details)> Entries { get; } = [];

    public void Record(
        Guid tenantId,
        Guid? templateId,
        Guid? requestId,
        string action,
        Guid? actorUserId,
        string? detailsJson = null
    ) => Entries.Add((action, "success", requestId, detailsJson));

    public void RecordSystemOutcome(
        Guid tenantId,
        Guid? templateId,
        Guid? requestId,
        string action,
        string outcome,
        string? detailsJson = null
    ) => Entries.Add((action, outcome, requestId, detailsJson));

    public void RecordStaffOutcome(
        Guid tenantId,
        Guid? templateId,
        Guid? requestId,
        string action,
        string outcome,
        Guid actorUserId,
        string? detailsJson = null,
        FormAuditRequestContext? requestContext = null
    ) => Entries.Add((action, outcome, requestId, detailsJson));

    public void RecordParticipant(
        Guid tenantId,
        Guid requestId,
        string action,
        FormActor actor,
        string? detailsJson = null
    ) => Entries.Add((action, "success", requestId, detailsJson));

    public Task RecordAnswersViewedAsync(
        Guid tenantId,
        Guid requestId,
        int revisionNumber,
        Guid actorUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    ) => Task.CompletedTask;
}

internal sealed class ExtVisibility(bool customerVisible) : IFormRequestVisibilityFilter
{
    public Task<bool> CanViewCustomerAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        Guid customerId,
        CancellationToken ct = default
    ) => Task.FromResult(canViewAll || customerVisible);

    public Task<bool> CanViewAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        FormRequest request,
        CancellationToken ct = default
    ) => Task.FromResult(true);
}

internal sealed class ExtCustomerEmails(Guid? customerId) : IClientFormsCustomerEmailDirectory
{
    internal List<string> Lookups { get; } = [];

    public Task<Guid?> FindNonArchivedCustomerIdAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken ct = default
    )
    {
        Lookups.Add(normalizedEmail);
        return Task.FromResult(customerId);
    }
}

/// <summary>Bus que ejecuta InvokeAsync con un delegado: el controller llama al handler real.</summary>
public class InvokingMessageBus : DispatchProxy
{
    private Func<object, object>? _invoke;

    internal static IMessageBus Create(Func<object, object> invoke)
    {
        var bus = Create<IMessageBus, InvokingMessageBus>();
        ((InvokingMessageBus)(object)bus)._invoke = invoke;
        return bus;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != "InvokeAsync")
            throw new NotSupportedException($"El doble de bus no implementa {targetMethod?.Name}.");
        return _invoke!(args![0]!);
    }
}
