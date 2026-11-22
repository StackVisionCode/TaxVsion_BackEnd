using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Shares;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// El <c>sid</c> del link viaja en la URL: la atribución solo guarda el envío si es de ese mismo código. Uno ajeno o
/// inventado se descarta (la atribución sigue), para que nadie le sume registros al envío de otro.
/// </summary>
public sealed class CreateReferralAttributionShareTests
{
    private const string PlaintextCode = "FRIEND-2026";

    [Fact]
    public async Task A_share_of_the_same_code_is_kept()
    {
        var setup = Setup();
        var shareId = Guid.NewGuid();
        setup.Shares.CodeByShare[shareId] = setup.CodeId;

        var attribution = await setup.RunAsync(shareId);

        Assert.Equal(shareId, attribution.ShareId);
    }

    [Fact]
    public async Task A_share_of_another_code_is_dropped()
    {
        var setup = Setup();
        var shareId = Guid.NewGuid();
        setup.Shares.CodeByShare[shareId] = Guid.NewGuid();

        var attribution = await setup.RunAsync(shareId);

        Assert.Null(attribution.ShareId);
    }

    [Fact]
    public async Task An_invented_share_is_dropped()
    {
        var attribution = await Setup().RunAsync(Guid.NewGuid());

        Assert.Null(attribution.ShareId);
    }

    private static Fixture Setup()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        return new Fixture(program, code);
    }

    private sealed class Fixture(
        TaxVision.Referrals.Domain.Programs.ReferralProgram program,
        TaxVision.Referrals.Domain.Codes.ReferralCode code
    )
    {
        public Guid CodeId => code.Id;
        public FakeShares Shares { get; } = new();

        public async Task<TaxVision.Referrals.Domain.Attributions.ReferralAttribution> RunAsync(Guid shareId)
        {
            var attributions = new InMemoryReferralAttributionRepository();
            var (result, _) = await CreateReferralAttributionHandler.Handle(
                new CreateReferralAttributionCommand(
                    GrowthTestData.RefereeTenantId,
                    program.Id,
                    PlaintextCode,
                    ReferralParticipantType.Tenant,
                    GrowthTestData.RefereeTenantId,
                    "attribution-share",
                    GrowthTestData.ActorId,
                    ReferralShareChannel.Copy,
                    shareId
                ),
                new InMemoryReferralProgramRepository(program),
                new InMemoryReferralCodeRepository(code),
                attributions,
                new FakeReferralCodeTokenHasher(PlaintextCode, code.CodeHash),
                new FakeReferralIdempotencyExecutor(attributions),
                Shares,
                new TaxVision.Growth.Api.IntegrationEvents.ReferralNoticeEvents(),
                new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc.AddMinutes(1))),
                CancellationToken.None
            );

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
            return Assert.Single(attributions.Items);
        }
    }

    private sealed class FakeShares : IReferralCodeShareRepository
    {
        public Dictionary<Guid, Guid> CodeByShare { get; } = [];

        public Task AddAsync(ReferralCodeShare share, CancellationToken ct = default) => Task.CompletedTask;

        public Task<Guid?> FindReferralCodeIdAsync(Guid shareId, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(CodeByShare.TryGetValue(shareId, out var codeId) ? codeId : null);
    }
}
