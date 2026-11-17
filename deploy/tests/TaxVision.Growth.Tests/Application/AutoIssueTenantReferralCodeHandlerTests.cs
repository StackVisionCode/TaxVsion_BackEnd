using BuildingBlocks.Results;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Codes.AutoIssueTenantReferralCode;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// Al completarse el alta, la oficina nueva recibe su código de referido sin pulsar "Regenerate". Un replay del evento
/// no crea otro; sin programa activo no se emite nada; si el owner ya generó uno, se queda el suyo.
/// </summary>
public sealed class AutoIssueTenantReferralCodeHandlerTests
{
    private static readonly Guid Office = Guid.Parse("30000000-0000-0000-0000-000000000077");
    private static readonly Guid OwnerUser = Guid.Parse("40000000-0000-0000-0000-000000000077");
    private static readonly DateTime CompletedAtUtc = GrowthTestData.NowUtc.AddMinutes(5);

    [Fact]
    public async Task New_office_gets_its_code()
    {
        var f = new Fixture(GrowthTestData.CreateActiveTenantReferralProgram());

        var result = await f.IssueAsync();

        Assert.Equal(AutoIssueTenantReferralCodeOutcome.Issued, result.Value);
        var code = Assert.Single(f.Codes.Items);
        Assert.Equal(ReferralCodeStatus.Active, code.Status);
        Assert.Equal(Office, code.OwnerId);
        Assert.Equal(OwnerUser, code.OwnerUserId);
        Assert.Equal($"referral-auto:{Office:N}", code.IdempotencyKey);
        // Cinco años, pero nunca más allá del fin del programa.
        Assert.Equal(f.Program.EndsAtUtc, code.ExpiresAtUtc);
    }

    [Fact]
    public async Task Replay_does_not_issue_a_second_code()
    {
        var f = new Fixture(GrowthTestData.CreateActiveTenantReferralProgram());
        await f.IssueAsync();

        var replay = await f.IssueAsync();

        Assert.True(replay.IsSuccess);
        Assert.Single(f.Codes.Items);
    }

    [Fact]
    public async Task No_active_program_issues_nothing()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        Assert.True(program.Suspend(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);
        var f = new Fixture(program);

        var result = await f.IssueAsync();

        Assert.Equal(AutoIssueTenantReferralCodeOutcome.NoActiveProgram, result.Value);
        Assert.Empty(f.Codes.Items);
    }

    [Fact]
    public async Task An_office_that_already_has_a_code_keeps_it()
    {
        var f = new Fixture(GrowthTestData.CreateActiveTenantReferralProgram());
        await f.IssueAsync(key: "owner-regenerated");

        var result = await f.IssueAsync();

        Assert.Equal(AutoIssueTenantReferralCodeOutcome.AlreadyHasCode, result.Value);
        Assert.Equal("owner-regenerated", Assert.Single(f.Codes.Items).IdempotencyKey);
    }

    private sealed class Fixture
    {
        private readonly InMemoryReferralProgramRepository _programs;
        private readonly FakeReferralIdempotencyExecutor _idempotency;

        public Fixture(ReferralProgram program)
        {
            Program = program;
            _programs = new InMemoryReferralProgramRepository(program);
            Codes = new InMemoryReferralCodeRepository();
            _idempotency = new FakeReferralIdempotencyExecutor(Codes);
        }

        public ReferralProgram Program { get; }
        public InMemoryReferralCodeRepository Codes { get; }

        /// <summary>Emite como lo hace el consumidor; <paramref name="key"/> simula otra emisión (p. ej. del owner).</summary>
        public Task<Result<AutoIssueTenantReferralCodeOutcome>> IssueAsync(string? key = null) =>
            AutoIssueTenantReferralCodeHandler.Handle(
                new AutoIssueTenantReferralCodeCommand(Office, OwnerUser, CompletedAtUtc) { IdempotencyKey = key },
                _programs,
                Codes,
                new ReferralCodeOwnerApplicationTests.KeyDerivedTokenGenerator(),
                new ReferralCodeOwnerApplicationTests.Sha256Hasher(),
                _idempotency,
                new FixedTimeProvider(new DateTimeOffset(CompletedAtUtc.AddSeconds(2))),
                CancellationToken.None
            );
    }
}
