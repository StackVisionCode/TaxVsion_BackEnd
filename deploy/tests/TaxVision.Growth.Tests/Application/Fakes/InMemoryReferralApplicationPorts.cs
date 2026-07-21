using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Qualifications;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Tests.Application.Fakes;

internal sealed class FakeReferralCodeTokenHasher(string expectedToken, string expectedHash) : IReferralCodeTokenHasher
{
    internal List<string> ReceivedTokens { get; } = [];

    public Result<string> Hash(string referralCode)
    {
        ReceivedTokens.Add(referralCode);
        return string.Equals(referralCode, expectedToken, StringComparison.Ordinal)
            ? Result.Success(expectedHash)
            : Result.Failure<string>(new Error("ReferralCode.Invalid", "The referral code is invalid."));
    }
}

internal sealed class FakeReferralCodeTokenGenerator(string clearText) : IReferralCodeTokenGenerator
{
    internal List<(Guid ProgramId, Guid OwnerId, string IdempotencyKey)> ReceivedInputs { get; } = [];

    public Result<ReferralCodeToken> Generate(Guid programId, Guid ownerId, string idempotencyKey)
    {
        ReceivedInputs.Add((programId, ownerId, idempotencyKey));
        return ReferralCodeToken.Create(clearText);
    }
}

internal sealed class InMemoryReferralProgramRepository(params ReferralProgram[] seed)
    : IReferralProgramRepository,
        IFakeReferralTransactionalResource
{
    private readonly List<ReferralProgram> _items = [.. seed];

    internal IReadOnlyCollection<ReferralProgram> Items => _items;

    public Task<ReferralProgram?> GetOwnedByIdAsync(
        Guid ownerTenantId,
        Guid programId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(_items.FirstOrDefault(program => program.Id == programId && program.TenantId == ownerTenantId));

    public Task<ReferralProgram?> GetForEvaluationAsync(Guid programId, CancellationToken ct = default) =>
        Task.FromResult(_items.FirstOrDefault(program => program.Id == programId));

    public Task AddAsync(ReferralProgram program, CancellationToken ct = default)
    {
        _items.Add(program);
        return Task.CompletedTask;
    }

    public object CaptureState() => _items.ToArray();

    public void RestoreState(object snapshot)
    {
        _items.Clear();
        _items.AddRange((ReferralProgram[])snapshot);
    }
}

internal sealed class InMemoryReferralCodeRepository(params ReferralCode[] seed)
    : IReferralCodeRepository,
        IFakeReferralTransactionalResource
{
    private readonly List<ReferralCode> _items = [.. seed];

    internal IReadOnlyCollection<ReferralCode> Items => _items;

    public Task<ReferralCode?> GetActiveOwnedAsync(
        Guid ownerTenantId,
        Guid programId,
        ReferralParticipantType ownerType,
        Guid ownerId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _items.FirstOrDefault(code =>
                code.TenantId == ownerTenantId
                && code.ProgramId == programId
                && code.OwnerType == ownerType
                && code.OwnerId == ownerId
                && code.Status == ReferralCodeStatus.Active
            )
        );

    public Task<ReferralCode?> ResolveByHashAsync(Guid programId, string codeHash, CancellationToken ct = default) =>
        Task.FromResult(
            _items.FirstOrDefault(code =>
                code.ProgramId == programId && string.Equals(code.CodeHash, codeHash, StringComparison.Ordinal)
            )
        );

    public Task AddAsync(ReferralCode referralCode, CancellationToken ct = default)
    {
        _items.Add(referralCode);
        return Task.CompletedTask;
    }

    public object CaptureState() => _items.ToArray();

    public void RestoreState(object snapshot)
    {
        _items.Clear();
        _items.AddRange((ReferralCode[])snapshot);
    }
}

internal sealed class InMemoryReferralAttributionRepository(params ReferralAttribution[] seed)
    : IReferralAttributionRepository,
        IFakeReferralTransactionalResource
{
    private readonly List<ReferralAttribution> _items = [.. seed];

    internal IReadOnlyCollection<ReferralAttribution> Items => _items;

    public Task<ReferralAttribution?> GetByIdAsync(
        Guid attributionId,
        Guid ownerTenantId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _items.FirstOrDefault(attribution =>
                attribution.Id == attributionId && attribution.TenantId == ownerTenantId
            )
        );

    public Task AddAsync(ReferralAttribution attribution, CancellationToken ct = default)
    {
        _items.Add(attribution);
        return Task.CompletedTask;
    }

    public object CaptureState() => _items.ToArray();

    public void RestoreState(object snapshot)
    {
        _items.Clear();
        _items.AddRange((ReferralAttribution[])snapshot);
    }
}

internal sealed class InMemoryReferralQualificationRepository
    : IReferralQualificationRepository,
        IFakeReferralTransactionalResource
{
    private readonly List<ReferralQualification> _items = [];

    internal IReadOnlyCollection<ReferralQualification> Items => _items;

    public Task AddAsync(ReferralQualification qualification, CancellationToken ct = default)
    {
        _items.Add(qualification);
        return Task.CompletedTask;
    }

    public object CaptureState() => _items.ToArray();

    public void RestoreState(object snapshot)
    {
        _items.Clear();
        _items.AddRange((ReferralQualification[])snapshot);
    }
}

internal sealed class InMemoryReferralRewardCaseRepository(params ReferralRewardCase[] seed)
    : IReferralRewardCaseRepository,
        IFakeReferralTransactionalResource
{
    private readonly List<ReferralRewardCase> _items = [.. seed];

    internal IReadOnlyCollection<ReferralRewardCase> Items => _items;

    public Task<ReferralRewardCase?> GetByIdAsync(
        Guid rewardCaseId,
        Guid ownerTenantId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(_items.FirstOrDefault(reward => reward.Id == rewardCaseId && reward.TenantId == ownerTenantId));

    public Task<ReferralRewardCase?> GetByGrantIdAsync(
        Guid grantId,
        Guid ownerTenantId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(_items.FirstOrDefault(reward => reward.GrantId == grantId && reward.TenantId == ownerTenantId));

    public Task<ReferralRewardCase?> GetForCompensationAsync(Guid rewardCaseId, CancellationToken ct = default) =>
        Task.FromResult(_items.FirstOrDefault(reward => reward.Id == rewardCaseId));

    public Task AddAsync(ReferralRewardCase rewardCase, CancellationToken ct = default)
    {
        _items.Add(rewardCase);
        return Task.CompletedTask;
    }

    public object CaptureState() => _items.ToArray();

    public void RestoreState(object snapshot)
    {
        _items.Clear();
        _items.AddRange((ReferralRewardCase[])snapshot);
    }
}

internal sealed class FakeReferralRewardQuota : IReferralRewardQuota, IFakeReferralTransactionalResource
{
    private readonly List<Guid> _qualificationReservations = [];

    internal int InvocationCount { get; private set; }
    internal IReadOnlyCollection<Guid> QualificationReservations => _qualificationReservations;
    internal bool SlotAvailable { get; set; } = true;

    public Task<bool> TryReserveAnnualSlotAsync(
        Guid programId,
        Guid referrerId,
        int calendarYear,
        int maximum,
        Guid qualificationId,
        CancellationToken ct = default
    )
    {
        InvocationCount++;
        if (SlotAvailable && !_qualificationReservations.Contains(qualificationId))
            _qualificationReservations.Add(qualificationId);

        return Task.FromResult(SlotAvailable);
    }

    public object CaptureState() => new Snapshot(InvocationCount, [.. _qualificationReservations]);

    public void RestoreState(object snapshot)
    {
        var state = (Snapshot)snapshot;
        InvocationCount = state.InvocationCount;
        _qualificationReservations.Clear();
        _qualificationReservations.AddRange(state.QualificationReservations);
    }

    private sealed record Snapshot(int InvocationCount, IReadOnlyCollection<Guid> QualificationReservations);
}
