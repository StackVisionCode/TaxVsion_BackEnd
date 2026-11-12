using BuildingBlocks.Results;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Codes.ValidateReferralCode;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// El landing valida el código del enlace antes del alta. Responde SOLO si sirve o no: un código reemplazado,
/// vencido, inexistente o mal formado da lo mismo (false), sin revelar nada de nadie.
/// </summary>
public sealed class ValidateReferralCodeHandlerTests
{
    private const string Token = "TVR-7K3Q9M2XPA";

    private static (ReferralProgram Program, ReferralCode Code) Setup()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        return (program, GrowthTestData.CreateReferralCode(program));
    }

    private static Task<Result<ValidateReferralCodeResult>> Run(
        string? token,
        ReferralProgram? program,
        ReferralCode? code,
        DateTime? now = null
    ) =>
        ValidateReferralCodeHandler.Handle(
            new ValidateReferralCodeQuery(token!),
            program is null ? new InMemoryReferralProgramRepository() : new InMemoryReferralProgramRepository(program),
            new HashOf(code),
            code is null ? new InMemoryReferralCodeRepository() : new InMemoryReferralCodeRepository(code),
            new FixedTimeProvider(new DateTimeOffset(now ?? GrowthTestData.NowUtc)),
            CancellationToken.None
        );

    /// <summary>Reconoce SOLO el token de prueba como el hash del código (como el hash real).</summary>
    private sealed class HashOf(ReferralCode? code) : IReferralCodeTokenHasher
    {
        public Result<string> Hash(string referralCode) =>
            code is not null && string.Equals(referralCode.Trim(), Token, StringComparison.OrdinalIgnoreCase)
                ? Result.Success(code.CodeHash)
                : Result.Success(new string('f', 64));
    }

    [Fact]
    public async Task An_active_code_is_valid()
    {
        var (program, code) = Setup();

        var result = await Run(Token, program, code);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task A_code_replaced_by_a_new_one_is_no_longer_valid()
    {
        var (program, code) = Setup();
        Assert.True(code.Supersede(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);

        var result = await Run(Token, program, code);

        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task An_expired_code_is_not_valid()
    {
        var (program, code) = Setup();

        var result = await Run(Token, program, code, now: code.ExpiresAtUtc.AddDays(1));

        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task An_unknown_code_is_not_valid_and_looks_the_same_as_a_revoked_one()
    {
        var (program, code) = Setup();

        var result = await Run("TVR-NOTACODE00", program, code);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_code_is_not_valid(string? token)
    {
        var (program, code) = Setup();

        var result = await Run(token, program, code);

        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task An_oversized_code_is_not_valid()
    {
        var (program, code) = Setup();

        var result = await Run(new string('A', 101), program, code);

        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task Without_an_active_program_nothing_validates()
    {
        var (_, code) = Setup();

        var result = await Run(Token, program: null, code);

        Assert.False(result.Value.IsActive);
    }
}
