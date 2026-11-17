using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Options;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Quotes.CreateQuote;
using TaxVision.Codes.Application.Reservations.ReserveCode;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Infrastructure.Security;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// G5: en el alta el límite por sujeto cuenta por COMPRADOR (su email normalizado), no por alta. Dos altas de la misma
/// persona, aunque use un alias, comparten el límite; un correo desechable no usa códigos; el límite por tenant no se
/// aplica en el alta (todavía no hay oficina).
/// </summary>
public sealed class ReserveCodeHandlerTests
{
    [Fact]
    public async Task Second_signup_with_the_same_email_hits_the_per_subject_limit()
    {
        var f = new Fixture(maxPerSubject: 1);

        Assert.True((await f.SignupAsync("ana@example.com")).IsSuccess);
        var second = await f.SignupAsync("ana@example.com");

        Assert.Equal("Codes.CodeUsageCounter.SubjectLimitReached", second.Error.Code);
    }

    [Fact]
    public async Task Second_signup_with_a_plus_alias_hits_the_same_limit()
    {
        var f = new Fixture(maxPerSubject: 1);

        Assert.True((await f.SignupAsync("ana@example.com")).IsSuccess);
        var alias = await f.SignupAsync("Ana+2@Example.com");

        Assert.Equal("Codes.CodeUsageCounter.SubjectLimitReached", alias.Error.Code);
    }

    [Fact]
    public async Task Quote_for_a_buyer_who_already_used_the_code_is_refused()
    {
        // La vista previa del alta cotiza sin reservar: debe avisar antes de llegar al pago.
        var f = new Fixture(maxPerSubject: 1);

        Assert.True((await f.SignupAsync("ana@example.com")).IsSuccess);
        var preview = await f.QuoteAsync("ana+2@example.com");

        Assert.True(preview.IsFailure);
        Assert.Equal("Codes.CodeUsageCounter.SubjectLimitReached", preview.Error.Code);
    }

    [Fact]
    public async Task Reservation_still_decides_when_both_quotes_came_before_the_first_use()
    {
        // La cotización solo avisa: dos vistas previas a la vez pasan, pero solo una reserva gana.
        var f = new Fixture(maxPerSubject: 1);
        var first = await f.QuoteAsync("ana@example.com");
        var second = await f.QuoteAsync("ana+2@example.com");

        Assert.True((await f.ReserveAsync(first.Value.QuoteId)).IsSuccess);
        var late = await f.ReserveAsync(second.Value.QuoteId);

        Assert.Equal("Codes.CodeUsageCounter.SubjectLimitReached", late.Error.Code);
    }

    [Fact]
    public async Task Different_people_do_not_share_the_limit()
    {
        var f = new Fixture(maxPerSubject: 1);

        Assert.True((await f.SignupAsync("ana@example.com")).IsSuccess);
        Assert.True((await f.SignupAsync("luis@example.com")).IsSuccess);
    }

    [Fact]
    public async Task Disposable_email_cannot_use_a_code()
    {
        var f = new Fixture(maxPerSubject: 1);

        var quote = await f.QuoteAsync("ana@mailinator.com");

        Assert.Equal("Codes.Buyer.DisposableEmail", quote.Error.Code);
    }

    [Fact]
    public async Task The_per_tenant_limit_does_not_apply_to_signups()
    {
        // Sin oficina todavía, todas las altas comparten el tenant de plataforma: contarlo las juntaría a todas.
        var f = new Fixture(maxPerSubject: null, maxPerTenant: 1);

        Assert.True((await f.SignupAsync("ana@example.com")).IsSuccess);
        Assert.True((await f.SignupAsync("luis@example.com")).IsSuccess);
    }

    private sealed class Fixture
    {
        private readonly CodeDefinition _definition;
        private readonly InMemoryCodeDefinitionRepository _definitions;
        private readonly InMemoryCodeQuoteRepository _quotes = new();
        private readonly InMemoryCodeReservationRepository _reservations = new();
        private readonly InMemoryCodeUsageCounterRepository _counters = new();
        private readonly FixedTimeProvider _clock = new(new DateTimeOffset(GrowthTestData.NowUtc));
        private int _signups;

        public Fixture(long? maxPerSubject, long? maxPerTenant = null)
        {
            _definition = GrowthTestData.CreateActivePercentageCode(
                maxRedemptions: 100,
                maxRedemptionsPerTenant: maxPerTenant,
                maxRedemptionsPerSubject: maxPerSubject
            );
            _definitions = new InMemoryCodeDefinitionRepository(_definition);
        }

        public async Task<Result<CreateQuoteResponse>> QuoteAsync(string buyerEmail)
        {
            var onboardingId = Guid.NewGuid();
            return await CreateQuoteHandler.Handle(
                new CreateQuoteCommand(
                    PlatformTenant.Id,
                    "WELCOME20",
                    SubjectType.Anonymous,
                    onboardingId.ToString("N"),
                    "Subscription",
                    "pro",
                    "v3",
                    10_000,
                    "USD",
                    GrowthTestData.Sha('c'),
                    $"quote-{onboardingId:N}",
                    600,
                    BuyerEmail: buyerEmail
                ),
                _definitions,
                _quotes,
                _counters,
                new FixedCodeTokenHasher(_definition.CodeHash),
                new HmacBuyerIdentityHasher(
                    Options.Create(new CodeTokenHashingOptions { Pepper = new string('p', 48) })
                ),
                new FileDisposableEmailDomains(),
                new FakeBusinessIdempotencyExecutor(),
                _clock,
                CancellationToken.None
            );
        }

        /// <summary>Un alta nueva: cotiza con su propio sujeto anónimo y reserva el código (el rechazo puede venir de cualquiera).</summary>
        public async Task<Result<ReserveCodeResponse>> SignupAsync(string buyerEmail)
        {
            var quote = await QuoteAsync(buyerEmail);
            if (quote.IsFailure)
                return Result.Failure<ReserveCodeResponse>(quote.Error);

            return await ReserveAsync(quote.Value.QuoteId);
        }

        public async Task<Result<ReserveCodeResponse>> ReserveAsync(Guid quoteId)
        {
            _signups++;
            return await ReserveCodeHandler.Handle(
                new ReserveCodeCommand(
                    PlatformTenant.Id,
                    quoteId,
                    "Onboarding",
                    Guid.NewGuid(),
                    $"reserve-{_signups}",
                    300
                ),
                _definitions,
                _quotes,
                _reservations,
                _counters,
                new FakeBusinessIdempotencyExecutor(),
                _clock,
                CancellationToken.None
            );
        }
    }

    private sealed class FixedCodeTokenHasher(CodeTokenHash hash) : ICodeTokenHasher
    {
        public Result<CodeTokenHash> Hash(string codeToken) => Result.Success(hash);
    }
}
