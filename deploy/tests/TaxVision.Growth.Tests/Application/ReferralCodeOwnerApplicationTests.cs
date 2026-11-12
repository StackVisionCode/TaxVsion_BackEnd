using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Codes.GetMyReferralCode;
using TaxVision.Referrals.Application.Codes.RecordReferralCodeShare;
using TaxVision.Referrals.Application.Codes.RegenerateTenantReferralCode;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Shares;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// Regla del dueño: el owner del tenant genera su código cuantas veces quiera (el anterior queda revocado y sin uso,
/// los referidos ya atribuidos conservan su premio); el resto de la oficina solo lo ve, lo copia y lo comparte, y cada
/// envío guarda su canal.
/// </summary>
public sealed class ReferralCodeOwnerApplicationTests
{
    private static readonly Guid Owner = GrowthTestData.ReferrerTenantId;
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(GrowthTestData.NowUtc));

    [Fact]
    public async Task First_generation_creates_an_active_code_owned_by_the_office_and_the_user_who_generated_it()
    {
        var f = Fixture.Create();
        var user = Guid.NewGuid();

        var result = await f.RegenerateAsync(user, "regen-1");

        Assert.True(result.IsSuccess);
        var code = Assert.Single(f.Codes.Items);
        Assert.Equal(ReferralCodeStatus.Active, code.Status);
        Assert.Equal(Owner, code.OwnerId);
        Assert.Equal(Owner, code.TenantId);
        Assert.Equal(user, code.OwnerUserId);
        Assert.Equal(f.Program.Id, code.ProgramId);
        Assert.Equal("regen-1", code.IdempotencyKey);
    }

    [Fact]
    public async Task Regenerating_revokes_the_previous_code_and_saves_the_revocation_before_inserting_the_new_one()
    {
        var f = Fixture.Create();
        await f.RegenerateAsync(Guid.NewGuid(), "regen-1");
        var first = f.Codes.Items.Single();
        var savesBefore = f.UnitOfWork.SaveCount;

        var second = await f.RegenerateAsync(Guid.NewGuid(), "regen-2");

        Assert.True(second.IsSuccess);
        Assert.Equal(2, f.Codes.Items.Count);
        Assert.Equal(ReferralCodeStatus.Revoked, first.Status);
        Assert.Equal(ReferralCode.SupersededReason, first.RevocationReason);
        var current = f.Codes.Items.Single(code => code.Status == ReferralCodeStatus.Active);
        Assert.Equal("regen-2", current.IdempotencyKey);
        Assert.NotEqual(first.CodeHash, current.CodeHash);
        // El índice único del código activo es filtrado: la revocación se confirma antes de insertar el nuevo.
        Assert.Equal(savesBefore + 1, f.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task The_previous_code_stops_registering_referrals_but_existing_attributions_keep_their_reward()
    {
        var f = Fixture.Create();
        await f.RegenerateAsync(Guid.NewGuid(), "regen-1");
        var oldCode = f.Codes.Items.Single();
        var attribution = ReferralAttribution
            .Create(
                f.Program,
                oldCode,
                ReferralParticipantType.Tenant,
                GrowthTestData.RefereeTenantId,
                "attribution-before",
                GrowthTestData.Sha('4'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc
            )
            .Value;
        Assert.True(attribution.Activate(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);

        await f.RegenerateAsync(Guid.NewGuid(), "regen-2");
        var afterReplacement = ReferralAttribution.Create(
            f.Program,
            oldCode,
            ReferralParticipantType.Tenant,
            Guid.NewGuid(),
            "attribution-after",
            GrowthTestData.Sha('5'),
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc
        );

        Assert.True(afterReplacement.IsFailure);
        Assert.Equal("ReferralCode.NotActive", afterReplacement.Error.Code);
        Assert.True(attribution.MarkQualified(GrowthTestData.ActorId, GrowthTestData.NowUtc.AddDays(1)).IsSuccess);
        Assert.Equal(ReferralAttributionStatus.Qualified, attribution.Status);
    }

    [Fact]
    public async Task Generation_is_idempotent_for_the_same_key()
    {
        var f = Fixture.Create();
        var user = Guid.NewGuid();

        var first = await f.RegenerateAsync(user, "regen-1");
        var replay = await f.RegenerateAsync(user, "regen-1");

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value.ReferralCodeId, replay.Value.ReferralCodeId);
        Assert.Single(f.Codes.Items);
    }

    [Fact]
    public async Task Generation_requires_an_active_program()
    {
        var f = Fixture.Create(withProgram: false);

        var result = await f.RegenerateAsync(Guid.NewGuid(), "regen-1");

        Assert.True(result.IsFailure);
        Assert.Equal("ReferralProgram.NotFound", result.Error.Code);
        Assert.Empty(f.Codes.Items);
    }

    [Fact]
    public async Task Expiry_never_exceeds_the_program_end()
    {
        var f = Fixture.Create();

        var result = await f.RegenerateAsync(Guid.NewGuid(), "regen-1");

        Assert.True(result.IsSuccess);
        Assert.True(f.Codes.Items.Single().ExpiresAtUtc <= f.Program.EndsAtUtc);
    }

    [Fact]
    public async Task Any_user_can_read_the_active_code_and_gets_the_seed_to_re_derive_it()
    {
        var f = Fixture.Create();
        await f.RegenerateAsync(Guid.NewGuid(), "regen-1");

        var mine = await GetMyReferralCodeHandler.Handle(
            new GetMyReferralCodeQuery(Owner),
            f.Programs,
            f.Codes,
            Clock,
            CancellationToken.None
        );

        Assert.True(mine.IsSuccess);
        Assert.Equal(f.Codes.Items.Single().Id, mine.Value.ReferralCodeId);
        Assert.Equal("regen-1", mine.Value.IdempotencyKey);
        Assert.Equal(ReferralCodeStatus.Active, mine.Value.Status);
    }

    [Fact]
    public async Task Reading_returns_the_new_code_after_regeneration_and_not_found_when_the_office_has_none()
    {
        var f = Fixture.Create();

        var none = await GetMyReferralCodeHandler.Handle(
            new GetMyReferralCodeQuery(Owner),
            f.Programs,
            f.Codes,
            Clock,
            CancellationToken.None
        );
        await f.RegenerateAsync(Guid.NewGuid(), "regen-1");
        await f.RegenerateAsync(Guid.NewGuid(), "regen-2");
        var current = await GetMyReferralCodeHandler.Handle(
            new GetMyReferralCodeQuery(Owner),
            f.Programs,
            f.Codes,
            Clock,
            CancellationToken.None
        );

        Assert.Equal("ReferralCode.NotFound", none.Error.Code);
        Assert.Equal("regen-2", current.Value.IdempotencyKey);
    }

    [Fact]
    public async Task One_office_cannot_read_another_offices_code()
    {
        var f = Fixture.Create();
        await f.RegenerateAsync(Guid.NewGuid(), "regen-1");

        var other = await GetMyReferralCodeHandler.Handle(
            new GetMyReferralCodeQuery(Guid.NewGuid()),
            f.Programs,
            f.Codes,
            Clock,
            CancellationToken.None
        );

        Assert.True(other.IsFailure);
        Assert.Equal("ReferralCode.NotFound", other.Error.Code);
    }

    [Theory]
    [InlineData(ReferralShareChannel.Copy)]
    [InlineData(ReferralShareChannel.WhatsApp)]
    [InlineData(ReferralShareChannel.Email)]
    public async Task Any_user_records_a_share_with_its_channel_without_modifying_the_code(ReferralShareChannel channel)
    {
        var f = Fixture.Create();
        await f.RegenerateAsync(Guid.NewGuid(), "regen-1");
        var code = f.Codes.Items.Single();
        var statusBefore = code.Status;
        var employee = Guid.NewGuid();

        var share = await f.ShareAsync(code.Id, channel, employee, "ana@cliente.com");

        Assert.True(share.IsSuccess);
        Assert.Equal(channel, share.Value.Channel);
        var stored = Assert.Single(f.Shares.Items);
        Assert.Equal(employee, stored.SharedByUserId);
        Assert.Equal(Owner, stored.TenantId);
        Assert.Equal(code.Id, stored.ReferralCodeId);
        Assert.Equal(statusBefore, code.Status);
    }

    [Fact]
    public async Task A_share_of_another_offices_code_or_of_a_revoked_code_is_rejected()
    {
        var f = Fixture.Create();
        await f.RegenerateAsync(Guid.NewGuid(), "regen-1");
        var oldCode = f.Codes.Items.Single();
        await f.RegenerateAsync(Guid.NewGuid(), "regen-2");

        var foreign = await f.ShareAsync(oldCode.Id, ReferralShareChannel.Sms, Guid.NewGuid(), null, Guid.NewGuid());
        var revoked = await f.ShareAsync(oldCode.Id, ReferralShareChannel.Sms, Guid.NewGuid(), null);

        Assert.Equal("ReferralCode.NotFound", foreign.Error.Code);
        Assert.Equal("ReferralCode.NotActive", revoked.Error.Code);
        Assert.Empty(f.Shares.Items);
    }

    private sealed class Fixture
    {
        public ReferralProgram Program { get; }
        public InMemoryReferralProgramRepository Programs { get; }
        public InMemoryReferralCodeRepository Codes { get; }
        public InMemoryShareRepository Shares { get; } = new();
        public CountingUnitOfWork UnitOfWork { get; } = new();
        private readonly KeyDerivedTokenGenerator _generator = new();
        private readonly Sha256Hasher _hasher = new();
        private readonly FakeReferralIdempotencyExecutor _idempotency;

        private Fixture(ReferralProgram program, bool withProgram)
        {
            Program = program;
            Programs = withProgram ? new InMemoryReferralProgramRepository(program) : new InMemoryReferralProgramRepository();
            Codes = new InMemoryReferralCodeRepository();
            _idempotency = new FakeReferralIdempotencyExecutor(Codes);
        }

        public static Fixture Create(bool withProgram = true) =>
            new(GrowthTestData.CreateActiveTenantReferralProgram(), withProgram);

        public Task<Result<TaxVision.Referrals.Application.Codes.IssueTenantReferralCode.IssueTenantReferralCodeResult>>
            RegenerateAsync(Guid actorUserId, string key) =>
            RegenerateTenantReferralCodeHandler.Handle(
                new RegenerateTenantReferralCodeCommand(Owner, actorUserId, key),
                Programs,
                Codes,
                _generator,
                _hasher,
                _idempotency,
                UnitOfWork,
                Clock,
                CancellationToken.None
            );

        public Task<Result<ReferralCodeShareResult>> ShareAsync(
            Guid codeId,
            ReferralShareChannel channel,
            Guid userId,
            string? hint,
            Guid? tenantId = null
        ) =>
            RecordReferralCodeShareHandler.Handle(
                new RecordReferralCodeShareCommand(tenantId ?? Owner, codeId, channel, userId, hint),
                Codes,
                Shares,
                UnitOfWork,
                Clock,
                CancellationToken.None
            );
    }

    private sealed class KeyDerivedTokenGenerator : IReferralCodeTokenGenerator
    {
        public Result<ReferralCodeToken> Generate(Guid programId, Guid ownerId, string idempotencyKey) =>
            ReferralCodeToken.Create($"TVR-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)))[..20]}");
    }

    private sealed class Sha256Hasher : IReferralCodeTokenHasher
    {
        public Result<string> Hash(string referralCode) =>
            Result.Success(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(referralCode))).ToLowerInvariant());
    }

    private sealed class InMemoryShareRepository : IReferralCodeShareRepository
    {
        private readonly List<ReferralCodeShare> _items = [];
        public IReadOnlyList<ReferralCodeShare> Items => _items;

        public Task AddAsync(ReferralCodeShare share, CancellationToken ct = default)
        {
            _items.Add(share);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }
}
