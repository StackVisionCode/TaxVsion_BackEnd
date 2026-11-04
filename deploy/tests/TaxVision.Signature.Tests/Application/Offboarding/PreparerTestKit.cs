using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Profiles.EffectiveSignature;
using TaxVision.Signature.Application.Requests.Preparer;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Permissions;
using TaxVision.Signature.Domain.Profiles;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Application.Offboarding;

/// <summary>Solicitudes en cada estado y dobles en memoria para el retiro y el cambio de preparador.</summary>
internal static class PreparerRequests
{
    public static readonly PreparerPermissionCodes Codes = new("signature.sign_own", "signature.request.manage");

    public static SignatureRequest Draft(Guid tenantId, Guid authorId)
    {
        var request = SignatureRequest
            .CreateDraft(
                TestSignatureLimits.Default,
                tenantId,
                authorId,
                "Form 8879 - 2025",
                null,
                "Fiscal",
                72,
                false,
                false,
                false
            )
            .Value;
        request.AddDocument(TestSignatureLimits.Default, Guid.NewGuid(), "Form 8879");
        return request;
    }

    public static SignatureRequest DraftWithPreparer(
        Guid tenantId,
        Guid authorId,
        Guid preparerId,
        Guid? signatureFileId = null
    )
    {
        var request = Draft(tenantId, authorId);
        request.SetPreparer(Identity(preparerId));
        if (signatureFileId is { } fileId)
            request.SetPreparerSignature(fileId);
        return request;
    }

    /// <summary>Enviada con un firmante y el preparador ligado a <paramref name="preparerId"/>.</summary>
    public static SignatureRequest Sent(
        Guid tenantId,
        Guid authorId,
        Guid? preparerId,
        Guid? signatureFileId = null,
        int signers = 1
    )
    {
        var request = preparerId is { } id
            ? DraftWithPreparer(tenantId, authorId, id, signatureFileId)
            : Draft(tenantId, authorId);
        var document = request.Documents.Single();
        for (var index = 0; index < signers; index++)
        {
            var signer = request
                .AddSigner(
                    TestSignatureLimits.Default,
                    SignerEmail.Create($"client{index}@example.com").Value,
                    SignerFullName.Create($"Client Number {index}").Value,
                    null
                )
                .Value;
            request.PlaceField(
                TestSignatureLimits.MaxFieldsPerSigner,
                signer.Id,
                document.Id,
                SignatureFieldKind.Signature,
                FieldPosition.Create(1, 0.1, 0.1 + index * 0.1, 0.2, 0.05).Value,
                null,
                false
            );
        }
        request.AttachVerifiedDocument(document.Id, VerifiedDocument.Create(Hash('a'), 50).Value);
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        return request;
    }

    public static PreparerInfo Identity(Guid userId, string ptin = "P12345678", string name = "Jane Doe, EA") =>
        PreparerInfo.Create(ptin, name, "Enrolled Agent", userId).Value;

    public static DocumentHash Hash(char value) => DocumentHash.Create(new string(value, 64)).Value;

    public static AuthzUserPermissionsProjection Member(
        Guid tenantId,
        Guid userId,
        bool active = true,
        params string[] codes
    )
    {
        var member = AuthzUserPermissionsProjection.Create(tenantId, userId, 1, codes, []);
        if (!active)
            member.MarkInactive();
        return member;
    }
}

/// <summary>Repositorio de trabajo del staff en memoria: misma semántica que el de EF.</summary>
internal sealed class InMemoryStaffWork : ISignatureRequestStaffWorkRepository
{
    public List<SignatureRequest> Requests { get; } = [];
    public Dictionary<Guid, PreparerIdentitySnapshot> Identities { get; } = [];

    public Task<IReadOnlyList<SignatureRequest>> ListUnsentAuthoredByAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<SignatureRequest>>(
            Requests.Where(r => r.TenantId == tenantId && r.CreatedByUserId == userId && IsUnsent(r)).ToList()
        );

    public Task<IReadOnlyList<SignatureRequest>> ListOpenWithPreparerAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<SignatureRequest>>(
            Requests
                .Where(r =>
                    r.TenantId == tenantId
                    && r.Preparer?.UserId == userId
                    && !r.IsPreparerSigned
                    && (IsUnsent(r) || r.Status == SignatureRequestStatus.InProgress)
                )
                .ToList()
        );

    public Task<SignatureOffboardingCounts> CountOffboardingImpactAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<PreparerIdentitySnapshot?> FindLatestPreparerIdentityAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => Task.FromResult(Identities.TryGetValue(userId, out var identity) ? identity : null);

    public Task<IReadOnlyList<Guid>> ListUsersWithPreparerIdentityAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<Guid>>(Identities.Keys.ToList());

#pragma warning disable CS0618
    private static bool IsUnsent(SignatureRequest r) =>
        r.Status is SignatureRequestStatus.Draft or SignatureRequestStatus.Ready or SignatureRequestStatus.Scheduled;
#pragma warning restore CS0618
}

internal sealed class InMemoryMembers : IAuthzUserPermissionsProjectionRepository
{
    public List<AuthzUserPermissionsProjection> Items { get; } = [];

    public Task<AuthzUserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.TenantId == tenantId && p.UserId == userId));

    public Task AddAsync(AuthzUserPermissionsProjection projection, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<AuthzUserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();
}

internal sealed class InMemoryProfiles : ISignatureProfileRepository
{
    public List<SignatureProfile> Items { get; } = [];

    public SignatureProfile Add(Guid tenantId, Guid? ownerUserId, bool isDefault = true)
    {
        var profile = SignatureProfile
            .Create(tenantId, Guid.NewGuid(), ownerUserId, "Signature", Guid.NewGuid(), 300, 100)
            .Value;
        if (isDefault)
            profile.MarkDefault();
        Items.Add(profile);
        return profile;
    }

    public Task<SignatureProfile?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.TenantId == tenantId && p.Id == id));

    public Task<SignatureProfile?> GetByFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.TenantId == tenantId && p.FileId == fileId));

    public Task<IReadOnlyList<SignatureProfile>> ListVisibleAsync(
        Guid tenantId,
        Guid userId,
        bool includePersonal,
        bool includeArchived,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<SignatureProfile>> ListByOwnerAsync(
        Guid tenantId,
        Guid? ownerUserId,
        bool includeArchived,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<SignatureProfile?> GetDefaultAsync(Guid tenantId, Guid? ownerUserId, CancellationToken ct = default) =>
        Task.FromResult(
            Items.FirstOrDefault(p =>
                p.TenantId == tenantId && p.OwnerUserId == ownerUserId && p.IsDefault && !p.IsArchived
            )
        );

    public Task AddAsync(SignatureProfile profile, CancellationToken ct = default) => throw new NotSupportedException();

    public void Remove(SignatureProfile profile) => throw new NotSupportedException();
}

/// <summary>Firma efectiva: la personal por defecto si puede usarla, si no la de oficina (como el real sin settings).</summary>
internal sealed class ProfileBackedEffectiveSignature(InMemoryProfiles profiles) : IEffectiveSignatureResolver
{
    public async Task<Result<SignatureProfile>> ResolveAsync(
        Guid tenantId,
        Guid preparerUserId,
        bool actorHasSignOwn,
        CancellationToken ct = default
    )
    {
        if (actorHasSignOwn && await profiles.GetDefaultAsync(tenantId, preparerUserId, ct) is { } personal)
            return Result.Success(personal);
        var office = await profiles.GetDefaultAsync(tenantId, null, ct);
        return office is not null
            ? Result.Success(office)
            : Result.Failure<SignatureProfile>(EffectiveSignatureResolver.None);
    }
}

/// <summary>Cadena de auditoría que registra lo agregado; puede forzarse a fallar.</summary>
internal sealed class ChainAudit : IAuditChainAppender
{
    public bool Fail { get; set; }
    public List<(Guid RequestId, SignatureAuditEventKind Kind, string PayloadJson)> Appended { get; } = [];

    public Task<Result<SignatureAuditEvent>> AppendAsync(
        Guid tenantId,
        Guid signatureRequestId,
        SignatureAuditEventKind kind,
        DateTime occurredAtUtc,
        object payload,
        CancellationToken ct = default
    )
    {
        if (Fail)
            return Task.FromResult(
                Result.Failure<SignatureAuditEvent>(new Error("Signature.Audit.Settings", "no secret"))
            );

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        Appended.Add((signatureRequestId, kind, json));
        var hash = new string('c', 64);
        return Task.FromResult(
            SignatureAuditEvent.Create(
                tenantId,
                signatureRequestId,
                Appended.Count,
                kind,
                occurredAtUtc,
                json,
                SignatureAuditEvent.GenesisMarker,
                hash
            )
        );
    }
}

internal sealed class CountingUnitOfWork : BuildingBlocks.Persistence.IUnitOfWork
{
    public int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Saves++;
        return Task.FromResult(0);
    }
}

internal sealed class RecordingListCache : ISignatureRequestListCacheInvalidator
{
    public int Invalidations { get; private set; }

    public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default)
    {
        Invalidations++;
        return Task.CompletedTask;
    }
}
